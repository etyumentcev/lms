import os,fcntl,struct,tempfile,json,errno,subprocess
out={'uid':os.getuid(),'kernel':os.uname().release,'probes':[]}
for base in ['/var/tmp','/tmp']:
 with tempfile.TemporaryDirectory(prefix='storage-profile-01-',dir=base) as root:
  p=root+'/original'
  with open(p,'xb') as w: w.write(b'SYNTHETIC STORAGE PROFILE\n'); w.flush(); os.fsync(w.fileno())
  os.chmod(p,0o600)
  fd=os.open(p,os.O_RDONLY)
  item={'base':base,'mode':oct(os.stat(p).st_mode & 0o777),'block_size':os.statvfs(p).f_bsize}
  flags=bytearray(8);fcntl.ioctl(fd,0x80086601,flags);item['inode_flags']=hex(struct.unpack('Q',flags)[0]);item['extents']=bool(struct.unpack('Q',flags)[0]&0x80000)
  for name,req,arg in [('measure',0xc0046686,bytearray(struct.pack('HH',0,32)+bytes(32))),('enable',0x40806685,bytearray(struct.pack('III',1,1,4096)+bytes(116)))]:
   try:fcntl.ioctl(fd,req,arg);item[name]={'return':0}
   except OSError as e:item[name]={'errno':e.errno,'name':errno.errorcode[e.errno]}
  os.close(fd);out['probes'].append(item)
print(json.dumps(out,indent=2))
