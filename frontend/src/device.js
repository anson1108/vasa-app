import { Capacitor, registerPlugin } from '@capacitor/core';
export const native = Capacitor.isNativePlatform();
export const device = registerPlugin('DeviceControl');
// Browser preview is deliberately isolated from AWS and cannot fabricate device authentication.
let demoState = 'stopped';
export async function request(action) {
  if (native) {
    const r = await device.request({ action });
    if (r.status !== 200) throw new Error(r.error || (r.status === 429 ? 'THROTTLED' : 'SERVICE_UNAVAILABLE'));
    return r;
  }
  if (action === 'start') { demoState = 'pending'; setTimeout(() => { demoState = 'running'; }, 5000); }
  if (action === 'stop') { demoState = 'stopping'; setTimeout(() => { demoState = 'stopped'; }, 5000); }
  return { state: demoState, observedAt: Math.floor(Date.now() / 1000), dns: {
    domain: 'my.contoso1.asia', status: demoState === 'running' ? 'synced' : demoState === 'pending' ? 'pending' : 'inactive',
    publicIp: demoState === 'running' ? '203.0.113.10' : null
  } };
}
