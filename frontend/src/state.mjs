export const labels = { running: '运行中', stopped: '已停止', pending: '正在启动', stopping: '正在停止',
  'shutting-down': '实例正在终止', terminated: '实例已终止', unknown: '状态未知' };
export const canToggle = (state, busy, fresh) => !busy && fresh && ['running', 'stopped'].includes(state);
export const delayFor = (state, dns) => ['pending','stopping'].includes(state)
  || (state === 'running' && ['pending','waiting_ip','stale'].includes(dns?.status)) ? 5000 : 20000;
export function dnsFeedback(dns, fresh, instanceState) {
  const result = (tone, text, hint = '') => ({ tone, text, hint });
  if (!fresh) return result('neutral', '等待刷新 DNS 状态');
  if (dns?.status === 'disabled') return result('neutral', '未启用自动解析');
  if (['stopped','stopping','terminated','shutting-down'].includes(instanceState))
    return result('neutral', '实例未运行 · 保留原解析');
  if (instanceState === 'running' && dns?.status === 'synced')
    return result('success', 'DNS 已更新成功', '可以打开 AnyConnect 连接。若暂时连不上，请稍候重试，DNS 缓存或 VPN 服务可能尚未就绪。');
  if (dns?.status === 'error') return result('warning', 'DNS 更新失败，正在等待重试', '后台会自动重试，成功后会在这里提示。');
  if (dns?.status === 'unavailable') return result('warning', '暂时无法确认 DNS 状态', '请稍后刷新，确认更新成功后再连接。');
  if (['pending','running'].includes(instanceState)) {
    const hint = instanceState === 'pending' ? '等待实例启动并分配公网 IP…'
      : dns?.status === 'waiting_ip' ? '等待分配公网 IPv4…' : '后台正在同步，请稍候，完成后会自动提示。';
    return result('loading', 'DNS 更新中…', hint);
  }
  return result('neutral', '等待后台同步');
}
export const messages = {
  CLOCK_SKEW: '手机时间不准确，请开启系统自动日期和时间。',
  DEVICE_REVOKED_OR_UNKNOWN: '此手机尚未注册或已被撤销，请在设置中导出公钥并注册。',
  BAD_SIGNATURE: '设备签名校验失败，请检查注册公钥和接口地址。',
  REPLAY_OR_REVOKED: '请求重复或设备已撤销，请刷新状态。',
  TRANSITION_IN_PROGRESS: '实例正在切换状态，请等待刷新完成。',
  INSTANCE_UNAVAILABLE: '实例已终止或正在终止，无法使用此开关。',
  SERVICE_UNAVAILABLE: '服务暂时不可用，请稍后刷新。',
  THROTTLED: '请求过于频繁，请稍后刷新。',
  NETWORK: '网络请求失败或超时，操作结果可能未知，请先刷新真实状态。',
  NOT_CONFIGURED: '请先完成一次性设备配置。'
};
