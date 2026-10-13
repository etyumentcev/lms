import socket,struct,threading,subprocess,pathlib,json
root=pathlib.Path('/tmp/b07c2f/proxysocket');root.mkdir(exist_ok=True);path=root/'.s.PGSQL.5432'
if path.exists():path.unlink()
listener=socket.socket(socket.AF_UNIX);listener.bind(str(path));listener.listen(1);listener.settimeout(30)
dropped=threading.Event();errors=[]
def exact(s,n):
 b=b''
 while len(b)<n:
  a=s.recv(n-len(b))
  if not a:raise EOFError()
  b+=a
 return b
def relay():
 try:
  client,_=listener.accept();up=socket.socket(socket.AF_UNIX);up.connect('/tmp/b07c2f/pgsocket/.s.PGSQL.5432')
  def forward():
   try:
    while True:
     b=client.recv(65536)
     if not b:break
     up.sendall(b)
   except (OSError,EOFError):pass
  t=threading.Thread(target=forward,daemon=True);t.start()
  try:
   while True:
    h=exact(up,5);n=struct.unpack('!I',h[1:])[0];assert 4<=n<=1024*1024;p=exact(up,n-4)
    if h[:1]==b'C' and p==b'COMMIT\x00':dropped.set();break
    client.sendall(h+p)
  finally:
   for s in [client,up]:
    try:s.shutdown(socket.SHUT_RDWR)
    except OSError:pass
    s.close()
   t.join(2)
 except Exception as e:errors.append(repr(e))
t=threading.Thread(target=relay,daemon=True);t.start()
p=subprocess.run(['/tmp/b07c2f/sdk/dotnet','run','--project','/tmp/b07c2f/probe/probe.csproj','--no-restore','--','wire'],text=True,capture_output=True,timeout=60)
t.join(3);listener.close();path.unlink();print(p.stdout);print(p.stderr);assert p.returncode==0 and dropped.is_set() and not errors,(p.returncode,dropped.is_set(),errors)
pathlib.Path('/tmp/b07c2f/wire-results.json').write_text(json.dumps(dict(command_complete_commit_dropped=True,exit_code=p.returncode,output=p.stdout),indent=2))
