<script setup>
import { ref, onMounted } from 'vue';

import {
  ElCard,
  ElForm,
  ElFormItem,
  ElSwitch,
  ElTimePicker,
  ElButton,
  ElAlert
} from 'element-plus';

import { native, scheduleRequest } from './device';
const enabled = ref(false), time = ref('23:00'), loading = ref(false), notice = ref(''), error = ref('');
const timezone = ref('Asia/Shanghai');
async function load() {
  if (!native) { notice.value = '浏览器演示：不会连接 AWS'; return; }
  loading.value = true; error.value = '';
  try { const r = await scheduleRequest({ action: 'schedule-status' }); enabled.value = !!r.enabled; time.value = r.time || '23:00'; timezone.value = r.timezone || timezone.value; }
  catch (e) { error.value = '读取云端定时设置失败：' + (e.message || 'NETWORK'); }
  finally { loading.value = false; }
}
async function save() {
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(time.value)) { error.value = '请输入 00:00 至 23:59 的时间'; return; }
  loading.value = true; error.value = ''; notice.value = '';
  try {
    const r = await scheduleRequest(enabled.value ? { action: 'schedule-set', time: time.value } : { action: 'schedule-disable' });
    enabled.value = !!r.enabled; time.value = r.time || time.value;
    notice.value = enabled.value ? `已同步到 AWS：每天 ${time.value} 自动停止 EC2` : '云端定时关闭已禁用';
  } catch (e) { error.value = '云端保存失败，未确认设置生效：' + (e.message || 'NETWORK'); }
  finally { loading.value = false; }
}
onMounted(load);
</script>
<template>
  <el-card class="schedule-settings" shadow="never">
    <template #header><strong>定时关闭 EC2</strong></template>
    <el-form label-position="top" @submit.prevent="save">
      <el-form-item label="启用每天定时关闭"><el-switch v-model="enabled" :disabled="loading" /></el-form-item>
      <el-form-item label="每天停止时间（24 小时制）">
        <el-time-picker v-model="time" value-format="HH:mm" format="HH:mm" placeholder="选择停止时间" :disabled="loading || !enabled" style="width:100%" />
      </el-form-item>
      <p class="schedule-note">时区：{{ timezone }}（UTC+08:00）。到达设定时间时，AWS 将尝试正常停止指定 EC2。</p>
      <el-button type="primary" native-type="submit" :loading="loading" :disabled="!native || loading">保存到 AWS</el-button>
      <el-button :disabled="loading" @click="load">读取云端设置</el-button>
      <el-alert v-if="notice" type="success" :title="notice" show-icon :closable="false" style="margin-top:12px" />
      <el-alert v-if="error" type="error" :title="error" show-icon :closable="false" style="margin-top:12px" />
    </el-form>
  </el-card>
</template>
<style scoped>.schedule-settings{margin-top:18px;border-radius:16px}.schedule-note{font-size:12px;color:#718091;line-height:1.7}</style>
