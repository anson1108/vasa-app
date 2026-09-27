import { test } from 'node:test';
import assert from 'node:assert/strict';
import { canToggle, delayFor, dnsFeedback } from '../src/state.mjs';
test('only fresh stable states allow a command', () => {
  for (const state of ['unknown','pending','stopping','terminated','shutting-down']) assert.equal(canToggle(state,false,true),false);
  for (const state of ['running','stopped']) {
    assert.equal(canToggle(state,false,true),true);
    assert.equal(canToggle(state,true,true),false);
    assert.equal(canToggle(state,false,false),false);
  }
});
test('DNS success is not shown for stale UI or failed synchronization', () => {
  assert.equal(dnsFeedback({status:'synced'},false,'running').tone,'neutral');
  assert.equal(dnsFeedback({status:'synced'},true,'stopped').tone,'neutral');
  assert.equal(dnsFeedback({status:'synced'},true,'stopping').tone,'neutral');
  assert.equal(dnsFeedback({status:'error'},true,'running').tone,'warning');
  assert.equal(dnsFeedback({status:'pending'},true,'running').tone,'loading');
  assert.equal(dnsFeedback({status:'waiting_ip'},true,'running').tone,'loading');
  assert.equal(dnsFeedback({status:'disabled'},true,'running').tone,'neutral');
  const success = dnsFeedback({status:'synced'},true,'running');
  assert.equal(success.tone,'success');
  assert.equal(success.text,'DNS 已更新成功');
  assert.match(success.hint,/AnyConnect/);
  assert.match(success.hint,/缓存/);
});
test('transitions poll faster than stable states', () => {
  assert.equal(delayFor('pending'),5000); assert.equal(delayFor('stopping'),5000); assert.equal(delayFor('running'),20000);
  assert.equal(delayFor('running',{status:'pending'}),5000);
  assert.equal(delayFor('running',{status:'synced'}),20000);
});
