"""Captura chamadas de I/O de hardware do GCUService.exe (app de controle do fabricante) via Frida.
Uso: python trace.py [pid|nome] [saida.log]"""
import sys, os, time, frida

out = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), sys.argv[2] if len(sys.argv) > 2 else "trace.log"), "a", buffering=1)

JS = r"""
const hex = (p, n) => { try { return p.readByteArray(Math.min(n, 96)) ? Array.from(new Uint8Array(p.readByteArray(Math.min(n,96)))).map(b=>('0'+b.toString(16)).slice(-2)).join(' ') : '' } catch(e){ return '<err>' } };
const names = {};
function hook(mod, fn, onEnter, onLeave) {
  const m = Process.findModuleByName(mod);
  if (!m) { send('nomodule ' + mod); return; }
  const a = m.findExportByName(fn);
  if (!a) { send('noexport ' + fn); return; }
  Interceptor.attach(a, { onEnter, onLeave }); send('hooked ' + fn);
}
hook('kernelbase.dll', 'CreateFileW', function(args){ this.p = args[0].readUtf16String(); }, function(r){
  if (this.p && /\\[.?]\|hid|UWACPI|\\Device/i.test(this.p)) { names[r.toString()] = this.p; send('CreateFile ' + this.p + ' -> ' + r); }
});
hook('kernelbase.dll', 'DeviceIoControl', function(args){
  const code = args[1].toInt32() >>> 0;
  const n = args[3].toInt32();
  send('IOCTL h=' + (names[args[0].toString()]||args[0]) + ' code=0x' + code.toString(16) + ' in[' + n + ']=' + hex(args[2], n));
  this.out = args[4]; this.on = args[5].toInt32(); this.ret = args[6];
}, function(r){
  try { const rn = this.ret.isNull() ? this.on : this.ret.readU32(); send('   -> ok=' + r.toInt32() + ' out[' + rn + ']=' + hex(this.out, rn)); } catch(e){}
});
hook('kernelbase.dll', 'WriteFile', function(args){
  const n = args[2].toInt32();
  send('WriteFile h=' + (names[args[0].toString()]||args[0]) + ' [' + n + ']=' + hex(args[1], n));
});
hook('kernelbase.dll', 'RegSetValueExW', function(args){
  try { const n = args[1].isNull() ? '' : args[1].readUtf16String(); const t = args[3].toInt32(); const sz = args[5].toInt32();
        send('REG ' + n + ' type=' + t + ' [' + sz + ']=' + hex(args[4], sz)); } catch(e){}
});
hook('kernelbase.dll', 'CreateProcessW', function(args){
  try { send('PROC ' + (args[0].isNull() ? '' : args[0].readUtf16String()) + ' | ' + (args[1].isNull() ? '' : args[1].readUtf16String())); } catch(e){}
});
hook('kernelbase.dll', 'CreateProcessInternalW', function(args){
  try { send('PROC ' + (args[1].isNull() ? '' : args[1].readUtf16String()) + ' | ' + (args[2].isNull() ? '' : args[2].readUtf16String())); } catch(e){}
});

hook('kernel32.dll', 'SetFirmwareEnvironmentVariableExW', function(args){
  try { const sz = args[3].toInt32(); send('UEFIW ' + args[0].readUtf16String() + ' guid=' + args[1].readUtf16String() + ' [' + sz + ']=' + hex(args[2], sz) + ' attr=' + args[4].toInt32()); } catch(e){}
});
hook('kernel32.dll', 'SetFirmwareEnvironmentVariableW', function(args){
  try { const sz = args[3].toInt32(); send('UEFIW ' + args[0].readUtf16String() + ' guid=' + args[1].readUtf16String() + ' [' + sz + ']=' + hex(args[2], sz)); } catch(e){}
});
hook('kernel32.dll', 'GetFirmwareEnvironmentVariableExW', function(args){
  try { this.n = args[0].readUtf16String(); this.g = args[1].readUtf16String(); this.b = args[2]; } catch(e){}
}, function(r){ try { send('UEFIR ' + this.n + ' guid=' + this.g + ' -> [' + r.toInt32() + ']=' + hex(this.b, r.toInt32())); } catch(e){} });
hook('ntdll.dll', 'NtSetSystemEnvironmentValueEx', function(args){ try { const sz = args[3].toInt32(); send('NTUEFI [' + sz + ']=' + hex(args[2], sz)); } catch(e){} });
const sa = Process.findModuleByName('setupapi.dll') || Module.load('setupapi.dll');
hook('setupapi.dll', 'SetupDiSetClassInstallParamsW', function(args){
  try { const p = args[2]; send('PNP SetClassInstallParams func=' + p.add(4).readU32() + ' state=' + p.add(8).readU32() + ' scope=' + p.add(12).readU32()); } catch(e){}
});
hook('setupapi.dll', 'SetupDiCallClassInstaller', function(args){ send('PNP CallClassInstaller func=' + args[0].toInt32()); });
const cm = Process.findModuleByName('cfgmgr32.dll') || Module.load('cfgmgr32.dll');
hook('cfgmgr32.dll', 'CM_Disable_DevNode', function(args){ send('PNP CM_Disable_DevNode'); });
hook('cfgmgr32.dll', 'CM_Enable_DevNode', function(args){ send('PNP CM_Enable_DevNode'); });
const wl = Process.findModuleByName('wlanapi.dll') || Module.load('wlanapi.dll');
hook('wlanapi.dll', 'WlanSetInterface', function(args){ const n = args[4].toInt32(); send('WLAN SetInterface opcode=' + args[3].toInt32() + ' [' + n + ']=' + hex(args[5], n)); });

const wm = Process.findModuleByName('wminet_utils.dll');
send('wminet_utils ' + (wm ? 'carregada' : 'ausente'));
if (wm) {
  // HRESULT Put(int vFunc, IWbemClassObject* obj, LPCWSTR name, LONG flags, VARIANT* val, CIMTYPE type)
  hook('wminet_utils.dll', 'Put', function(args){
    try {
      const name = args[2].isNull() ? '' : args[2].readUtf16String();
      const v = args[4]; let desc = '';
      if (!v.isNull()) { const vt = v.readU16(); desc = 'vt=' + vt + ' raw=' + hex(v.add(8), 8); if (vt == 8 && !v.add(8).readPointer().isNull()) desc += ' str=' + v.add(8).readPointer().readUtf16String(); }
      send('WMIPUT ' + name + ' ' + desc);
    } catch(e){ send('WMIPUT err ' + e); }
  });
  hook('wminet_utils.dll', 'ExecQueryWmi', function(args){ try { send('WMIQUERY ' + args[1].readUtf16String()); } catch(e){} });
}
"""

def on_message(msg, data):
    line = "%.3f %s" % (time.time(), msg.get("payload", msg))
    print(line); out.write(line + "\n")

import os
dev = frida.get_local_device()
names = [n.lower() for n in sys.argv[1].split(",")] if len(sys.argv) > 1 else ["gcuservice.exe"]
sessions = []
for p in dev.enumerate_processes():
    if p.name.lower() in names:
        try:
            ss = dev.attach(p.pid)
            sc = ss.create_script(JS)
            sc.on("message", (lambda pn: lambda m, d: on_message({"payload": "[%s] %s" % (pn, m.get("payload", m))}, d))(p.name + str(p.pid)))
            sc.load(); sessions.append(ss)
            out.write("attached %s %s\n" % (p.name, p.pid)); print("attached", p.name, p.pid)
        except Exception as e:
            print("fail", p.name, p.pid, e); out.write("fail %s %s %s\n" % (p.name, p.pid, e))
stop = os.path.join(os.path.dirname(os.path.abspath(__file__)), "STOP")
if os.path.exists(stop): os.remove(stop)
print("tracing; create tools/STOP to end")
while not os.path.exists(stop):
    time.sleep(1)
