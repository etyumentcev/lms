#!/usr/bin/env python3
"""Synthetic G02/A03 counterexamples; never an LMS adapter or T04 acceptance test."""
import os,sys,json,time,socket,struct,subprocess,tempfile,pathlib,hashlib,signal
OUT=pathlib.Path(sys.argv[1] if len(sys.argv)>1 else '/tmp/lms-g02-a03/run'); OUT.mkdir(parents=True,exist_ok=True)
rows=[]
def record(name,**kw):
    rows.append(dict(id=name,**kw)); (OUT/'results.json').write_text(json.dumps(rows,indent=2)); print(json.dumps(rows[-1]),flush=True)
def run(*args,**kw): return subprocess.check_output(args,text=True,timeout=30,**kw).strip()
def until(fn,limit=5):
    end=time.monotonic()+limit
    while time.monotonic()<end:
        x=fn()
        if x:return x
        time.sleep(.01)
    raise TimeoutError('oracle timeout')
def child(code,*args):return subprocess.Popen([sys.executable,'-u','-c',code,*map(str,args)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
with tempfile.TemporaryDirectory(prefix='g02-native-') as d:
    d=pathlib.Path(d)
    for kind,code,held in [
        ('open',"import os,sys; os.open(sys.argv[1],os.O_RDONLY)",None),
        ('read',"import os,sys; f=os.open(sys.argv[1],os.O_RDONLY); os.read(f,1)",'hold'),
        ('write',"import os,sys; f=os.open(sys.argv[1],os.O_WRONLY);\nwhile True: os.write(f,b'x'*65536)",'hold')]:
        fifo=d/kind;os.mkfifo(fifo);fd=os.open(fifo,os.O_RDWR|os.O_NONBLOCK) if held else None
        p=child(code,fifo)
        try:
            w=until(lambda:pathlib.Path(f'/proc/{p.pid}/wchan').read_text().strip() if pathlib.Path(f'/proc/{p.pid}/wchan').exists() else '')
            time.sleep(.05);w=pathlib.Path(f'/proc/{p.pid}/wchan').read_text().strip(); assert p.poll() is None and ('wait' in w or 'pipe' in w),w
            t=time.monotonic();p.kill();p.wait(timeout=5)
            record('fifo-'+kind,pid=p.pid,wchan=w,exit=p.returncode,kill_reap_ms=(time.monotonic()-t)*1000,scope='interruptible surrogate only')
        finally:
            if p.poll() is None:p.kill();p.wait()
            if fd is not None:os.close(fd)
    p=child("import time; print('ready',flush=True);\nwhile True: pass")
    try:
        assert p.stdout.readline().strip()=='ready';t=time.monotonic();p.kill();p.wait(timeout=5)
        record('userspace-hang',exit=p.returncode,kill_reap_ms=(time.monotonic()-t)*1000)
    finally:
        if p.poll() is None:p.kill();p.wait()
    obj=d/'object';epoch=d/'epoch';epoch.write_text('1')
    p=child("import os,sys,pathlib; assert pathlib.Path(sys.argv[2]).read_text()=='1'; f=os.open(sys.argv[1],os.O_WRONLY|os.O_CREAT,0o600);print('checked',flush=True);sys.stdin.readline();os.write(f,b'late');os.fsync(f);os.close(f)",obj,epoch)
    try:
        assert p.stdout.readline().strip()=='checked';epoch.write_text('2');declared=time.monotonic_ns();p.stdin.write('\n');p.stdin.flush();p.wait(timeout=5)
        assert obj.read_bytes()==b'late'
        record('generation-toctou',generation=1,new_epoch=2,declared_fence_ns=declared,observed_mutation_ns=time.monotonic_ns(),bytes=obj.stat().st_size,sha256=hashlib.sha256(obj.read_bytes()).hexdigest(),result='COUNTEREXAMPLE: token check is not mutation fence')
    finally:
        if p.poll() is None:p.kill();p.wait()
    fd=os.open(obj,os.O_WRONLY);sealed=d/'sealed';obj.rename(sealed);sealed.chmod(0o400);os.write(fd,b'EDIT');os.fsync(fd);os.close(fd)
    assert sealed.read_bytes()==b'EDIT';record('retained-fd-seal',result='COUNTEREXAMPLE: rename/chmod does not revoke writable FD',sha256=hashlib.sha256(sealed.read_bytes()).hexdigest())
# TCP local receive after sender close is not proof of late NIC send.
listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen();c=socket.create_connection(listener.getsockname());s,_=listener.accept();payload=b'x'*65536;s.sendall(payload);s.close();closed=time.monotonic_ns();time.sleep(.1);body=b''
while True:
    b=c.recv(65536)
    if not b:break
    body+=b
c.close();listener.close();assert body==payload
record('tcp-close-buffer',close_ns=closed,last_receive_ns=time.monotonic_ns(),bytes_received_after_close=len(body),result='COUNTEREXAMPLE to close == client receive completion; NIC boundary unobserved')
# Real disposable PG with private anonymous volume, no LMS database/config access.
PG='sha256:ef257d85f76e48da1c64832459b59fcaba1a4dac97bf5d7450c77753542eee94'
NG='sha256:2a9ff2bed2452490f7ac6cc2c64ad6ec8c22aad2ee71d89d7b4705f916fdda14'
class Conn:
    def __init__(self,port):
        self.s=socket.create_connection(('127.0.0.1',port),timeout=5)
        b=struct.pack('!I',196608)+b'user\0postgres\0database\0postgres\0application_name\0g02-synthetic\0\0';self.s.sendall(struct.pack('!I',len(b)+4)+b);self.read()
    def exact(self,n):
        b=b''
        while len(b)<n:
            x=self.s.recv(n-len(b))
            if not x:raise EOFError()
            b+=x
        return b
    def read(self):
        rows=[]
        while True:
            typ=self.exact(1);b=self.exact(struct.unpack('!I',self.exact(4))[0]-4)
            if typ==b'E':raise RuntimeError(repr(b))
            if typ==b'D':
                n=struct.unpack('!H',b[:2])[0];pos=2;r=[]
                for _ in range(n):
                    l=struct.unpack('!i',b[pos:pos+4])[0];pos+=4;r.append(None if l<0 else b[pos:pos+l].decode());pos+=max(l,0)
                rows.append(r)
            if typ==b'Z':return rows
    def send(self,q):
        b=q.encode()+b'\0';self.s.sendall(b'Q'+struct.pack('!I',len(b)+4)+b)
    def q(self,q):self.send(q);return self.read()
    def close(self):self.s.close()
containers=[]
try:
    pg=run('docker','run','-d','--label','lms.g02=a03-synthetic','-e','POSTGRES_HOST_AUTH_METHOD=trust','-p','127.0.0.1::5432',PG,'-c','fsync=on','-c','synchronous_commit=on','-c','full_page_writes=on');containers.append(pg)
    port=int(run('docker','port',pg,'5432/tcp').rsplit(':',1)[1])
    until(lambda:subprocess.run(['docker','exec',pg,'pg_isready','-h','127.0.0.1','-U','postgres'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0,20)
    a=Conn(port);o=Conn(port)
    record('pg-config',settings=o.q("select version(),current_setting('fsync'),current_setting('synchronous_commit'),current_setting('full_page_writes'),current_setting('wal_sync_method'),pg_is_in_recovery(),(select system_identifier::text from pg_control_system()),(select timeline_id from pg_control_checkpoint())"))
    a.q("create table operations(id text primary key, generation int not null, state text not null);create table effects(op text primary key references operations(id));insert into operations values ('lost',1,'Registered'),('rollback',1,'Registered'),('stopped',1,'Registered')")
    a.q("begin;select * from operations where id='lost' for update;insert into effects values ('lost');update operations set state='Committed' where id='lost'")
    false_absence=o.q("select count(*) from effects where op='lost'");assert false_absence==[['0']]
    t=time.monotonic_ns();a.send('COMMIT') # intentionally never read this client's COMMIT response
    until(lambda:o.q("select state from operations where id='lost'")==[['Committed']]);a.close()
    r=o.q("begin;select * from operations where id='lost' for update;select count(*) from effects where op='lost';commit")
    r2=o.q("begin;select * from operations where id='lost' for update;select count(*) from effects where op='lost';commit")
    assert r==r2 and r[-1]==['1']
    record('pg-lost-ack',commit_sent_ns=t,outcome_observed_ns=time.monotonic_ns(),client_commit_response_read=False,false_absence_before_commit=false_absence,serialized_resolve_twice=r,result='Committed exactly one effect; no replay')
    a=Conn(port);a.q("begin;insert into effects values ('rollback');rollback");assert o.q("select count(*) from effects where op='rollback'")==[['0']];record('pg-rollback',result='confirmed ROLLBACK, fresh primary sees zero effects');a.close()
    a=Conn(port);pid=int(a.q('select pg_backend_pid()')[0][0]);a.q("begin;insert into effects values ('stopped')")
    run('docker','exec',pg,'kill','-STOP',str(pid))
    try:
        t=time.monotonic();sent=o.q(f'select pg_terminate_backend({pid})');present=o.q(f'select count(*) from pg_stat_activity where pid={pid}');assert sent==[['t']] and present==[['1']]
        record('pg-terminate-signal',pid=pid,signal_result=sent,backend_still_present=present,elapsed_ms=(time.monotonic()-t)*1000,result='COUNTEREXAMPLE: signal acknowledgement is not process exit; SIGSTOP surrogate, not WAL fault')
    finally:run('docker','exec',pg,'kill','-CONT',str(pid))
    until(lambda:o.q(f'select count(*) from pg_stat_activity where pid={pid}')==[['0']]);assert o.q("select count(*) from effects where op='stopped'")==[['0']];a.close();o.close()
    run('docker','restart',pg)
    port=int(run('docker','port',pg,'5432/tcp').rsplit(':',1)[1])
    until(lambda:subprocess.run(['docker','exec',pg,'pg_isready','-h','127.0.0.1','-U','postgres'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0,20)
    o=Conn(port);history=o.q('select * from effects');assert history==[['lost']];record('pg-clean-restart',effects=history,result='history survived clean container restart; not power-loss or failover proof');o.close()
    (OUT/'pg.log').write_text(run('docker','logs',pg,stderr=subprocess.STDOUT))
    conf=OUT/'nginx.conf';conf.write_text('events {}\nhttp { access_log /dev/stdout; error_log /dev/stderr; server { listen 8080; location / { proxy_pass http://127.0.0.1:8081; proxy_buffering off; proxy_read_timeout 1s; send_timeout 1s; } } }\n')
    ng=run('docker','run','-d','--label','lms.g02=a03-synthetic','-p','127.0.0.1::8080','-v',str(conf.resolve())+':/etc/nginx/nginx.conf:ro','--entrypoint','nginx',NG,'-g','daemon off;');containers.append(ng)
    port=int(run('docker','port',ng,'8080/tcp').rsplit(':',1)[1])
    upstream=subprocess.Popen(['docker','exec',ng,'sh','-c',"{ printf 'HTTP/1.1 200 OK\r\nContent-Length: 10\r\nConnection: close\r\n\r\n'; for i in 1 2 3 4 5 6 7 8 9 10; do printf x; sleep 0.2; done; } | nc -l -p 8081"],stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    try:
        time.sleep(.2);s=socket.create_connection(('127.0.0.1',port),timeout=5);start=time.monotonic();s.sendall(b'GET / HTTP/1.0\r\nHost: synthetic\r\n\r\n');data=b''
        while True:
            b=s.recv(4096)
            if not b:break
            data+=b
        duration=time.monotonic()-start;s.close();assert data.split(b'\r\n\r\n',1)[1]==b'x'*10 and duration>1
        record('nginx-trickle',duration_seconds=duration,proxy_read_timeout_seconds=1,body_bytes=10,result='COUNTEREXAMPLE: ongoing traffic exceeds inactivity timeout total')
    finally:
        upstream.communicate(timeout=10)
    # Separate buffering-on/backpressure run, same nginx binary and namespace.
    conf.write_text(conf.read_text().replace('proxy_buffering off','proxy_buffering on'))
    run('docker','exec',ng,'nginx','-s','reload')
    time.sleep(.2)
    upstream=subprocess.Popen(['docker','exec',ng,'sh','-c',"{ printf 'HTTP/1.1 200 OK\\r\\nContent-Length: 131072\\r\\nConnection: close\\r\\n\\r\\n'; head -c 131072 /dev/zero; } | nc -l -p 8081"],stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    try:
        time.sleep(.2);s=socket.create_connection(('127.0.0.1',port),timeout=5);s.sendall(b'GET / HTTP/1.0\r\nHost: synthetic\r\n\r\n')
        # Client deliberately stops reading while upstream/nginx may enqueue bytes.
        time.sleep(.3);run('docker','kill',ng);cut=time.monotonic_ns();data=b'';end='eof'
        try:
            while True:
                b=s.recv(4096)
                if not b:break
                data+=b
        except (ConnectionResetError,socket.timeout) as ex:end=type(ex).__name__
        finally:s.close()
        assert len(data)>0
        record('nginx-buffered-cutoff',cutoff_observed_ns=cut,last_receive_ns=time.monotonic_ns(),bytes_received_after_container_kill=len(data),socket_end=end,result='queued bytes received after nginx stop; physical NIC last-send NOT instrumented')
    finally:upstream.communicate(timeout=10)
    (OUT/'nginx.log').write_text(run('docker','logs',ng,stderr=subprocess.STDOUT))
finally:
    for cid in reversed(containers):run('docker','rm','-f','-v',cid)
    record('cleanup',removed_containers=containers)
