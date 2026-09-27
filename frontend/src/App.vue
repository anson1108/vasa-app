<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { ElAlert, ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElIcon, ElInput, ElSwitch, ElTag } from 'element-plus';
import { Loading, Refresh, Setting } from '@element-plus/icons-vue';
import { device, native, request } from './device';
import { labels, canToggle, delayFor, messages, dnsFeedback } from './state.mjs';
const state = ref('unknown'), busy = ref(false), fresh = ref(false), error = ref('');
const last = ref('尚未刷新'), settings = ref(false), endpoint = ref(''), identity = ref(''), saved = ref(false);
const hardware = ref(''), configured = ref(!native);
const dns = ref(null);
let timer, disposed = false;
const enabled = computed(() => canToggle(state.value, busy.value, fresh.value));
const statusText = computed(() => fresh.value ? labels[state.value] || labels.unknown : '等待确认真实状态');
const dnsNotice = computed(() => dnsFeedback(dns.value, fresh.value, state.value));
function schedule() {
  clearTimeout(timer);
  if (!disposed && !document.hidden && configured.value) timer = setTimeout(refresh, delayFor(state.value, dns.value));
}
function showError(e) { error.value = messages[e.message] || '操作未完成，请检查网络和配置后重试刷新。'; }
async function refresh() {
  if (busy.value || !configured.value || disposed) return;
  busy.value = true;
  try {
    let result;
    for (let attempt = 0; attempt < 3; attempt++) {
      try { result = await request('status'); break; }
      catch (e) {
        if (attempt === 2 || !['NETWORK','SERVICE_UNAVAILABLE','THROTTLED'].includes(e.message)) throw e;
        await new Promise(resolve => setTimeout(resolve, 700 * 2 ** attempt));
        if (disposed || document.hidden) return;
      }
    }
    state.value = result.state; fresh.value = true; error.value = '';
    dns.value = result.dns || null;
    last.value = new Date(result.observedAt * 1000).toLocaleTimeString('zh-CN');
  } catch (e) { fresh.value = false; showError(e); }
  finally { busy.value = false; schedule(); }
}
async function toggle() {
  if (!enabled.value) return;
  const action = state.value === 'running' ? 'stop' : 'start';
  busy.value = true; fresh.value = false; error.value = ''; clearTimeout(timer);
  try {
    // Commands are never blindly retried; a timeout may mean AWS already accepted the action.
    const result = await request(action);
    state.value = result.state; fresh.value = true;
    dns.value = result.dns || null;
    last.value = new Date(result.observedAt * 1000).toLocaleTimeString('zh-CN');
  } catch (e) { showError(e); }
  finally { busy.value = false; schedule(); }
}
// Keep the control tied to AWS state instead of optimistically flipping it on click.
function requestToggle() { void toggle(); return false; }
async function save() {
  if (!native || busy.value) return;
  try {
    await device.configure({ endpoint: endpoint.value.trim() });
    configured.value = true; saved.value = true; fresh.value = false;
    await refresh();
  } catch (e) { error.value = '地址无效：请填写部署输出的马来西亚 API Gateway HTTPS 基础地址。'; }
}
function visibility() {
  clearTimeout(timer);
  fresh.value = false;
  if (!document.hidden) refresh();
}
onMounted(async () => {
  document.addEventListener('visibilitychange', visibility);
  if (native) {
    try {
      const info = await device.info();
      identity.value = JSON.stringify({ version: 1, deviceId: info.deviceId, publicKey: info.publicKey }, null, 2);
      hardware.value = info.hardwareBacked ? '硬件支持的 Android Keystore' : 'Android Keystore（此设备未报告硬件支持）';
      endpoint.value = info.endpoint || ''; configured.value = !!info.endpoint;
      settings.value = !configured.value;
    } catch (e) { error.value = '无法创建或读取设备密钥，请检查设备系统。'; return; }
  }
  refresh();
});
onUnmounted(() => { disposed = true; clearTimeout(timer); document.removeEventListener('visibilitychange', visibility); });
</script>

<template>
  <main>
    <header><div class="brand">EC2<span> / PERSONAL</span></div><el-button :icon="Setting" round @click="settings = !settings" :disabled="busy" aria-label="设备设置">设置</el-button></header>
    <el-alert v-if="!native" class="demo" title="浏览器演示 · 模拟状态，不会连接 AWS" type="warning" show-icon :closable="false" />
    <section class="control">
      <div class="eyebrow">AWS · MALAYSIA</div><h1>我的云主机</h1>
      <p class="subtitle">一个开关，随用随开。</p>
      <el-tag :type="state === 'running' && fresh ? 'success' : 'info'" effect="light" round size="large">{{ statusText }}</el-tag>
      <div class="power-wrap"><el-switch class="power-switch" :model-value="state === 'running' && fresh"
        :before-change="requestToggle" :disabled="!enabled" :loading="busy" :width="152" size="large"
        :aria-label="state === 'running' ? '停止虚拟机' : '启动虚拟机'" /></div>
      <p class="hint">{{ busy ? '正在确认…' : state === 'pending' || state === 'stopping' ? '请稍候，状态将自动刷新' : enabled ? (state === 'running' ? '点击开关停止实例' : '点击开关启动实例') : '确认状态后即可操作' }}</p>
      <el-alert v-if="error" class="error" :title="error" type="error" show-icon :closable="false" />
      <el-button class="refresh" text type="primary" :icon="Refresh" @click="refresh" :loading="busy" :disabled="busy || !configured">{{ busy ? '刷新中…' : '刷新状态 / 重试' }}</el-button>
      <p class="updated">上次确认 {{ last }}</p>
    </section>
    <el-card class="instance" shadow="never"><el-descriptions :column="1" size="small">
      <el-descriptions-item label="区域">ap-southeast-5</el-descriptions-item>
      <el-descriptions-item label="实例"><code>i-056494d14ab6b3dd0</code></el-descriptions-item>
    </el-descriptions></el-card>
    <el-card class="instance dns" shadow="never"><el-descriptions :column="1" size="small">
      <el-descriptions-item label="VPN 域名">{{ dns?.domain || 'my.contoso1.asia' }}</el-descriptions-item>
      <el-descriptions-item v-if="fresh && dns?.publicIp" label="公网 IPv4"><code>{{ dns.publicIp }}</code></el-descriptions-item>
      </el-descriptions>
      <section class="dns-feedback" role="status" aria-live="polite" aria-atomic="true">
        <div v-if="dnsNotice.tone === 'loading'" class="dns-loading">
          <el-icon class="is-loading" :size="26" aria-hidden="true"><Loading /></el-icon>
          <section><strong>{{ dnsNotice.text }}</strong><p>{{ dnsNotice.hint }}</p></section>
        </div>
        <el-alert v-else :title="dnsNotice.text" :description="dnsNotice.hint"
          :type="dnsNotice.tone === 'success' ? 'success' : dnsNotice.tone === 'warning' ? 'warning' : 'info'"
          show-icon :closable="false" />
      </section>
    </el-card>
    <p class="footnote">关闭开关仅停止实例，不会终止或删除实例。</p>
    <el-card class="setup" v-if="settings" shadow="never">
      <template #header><strong>一次性设备配置</strong></template><p>完成注册后，日常打开 App 无需登录。</p>
      <el-form label-position="top" @submit.prevent="save">
        <el-form-item label="API 基础地址" for="endpoint"><el-input id="endpoint" v-model="endpoint" placeholder="https://xxxx.execute-api.ap-southeast-5.amazonaws.com" :disabled="!native || busy" autocapitalize="off" spellcheck="false" /></el-form-item>
        <el-form-item><el-button type="primary" native-type="submit" :disabled="!native || busy">保存并刷新</el-button></el-form-item>
        <el-alert v-if="saved" title="地址已保存。若提示未注册，请继续完成公钥注册。" type="info" :closable="false" />
        <el-form-item label="设备注册 JSON（公钥，可复制）" for="identity"><el-input id="identity" type="textarea" readonly :model-value="identity || '在 Android 真机上打开 App 后生成。'" :rows="8" /></el-form-item>
      </el-form>
      <p>{{ hardware }}</p><p>将以上完整 JSON 保存为 device.json，在可信电脑上运行 README 中的注册命令。不要上传 AWS 密钥。</p>
    </el-card>
  </main>
</template>
