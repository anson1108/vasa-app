# 设备签名协议 v1

唯一业务接口为 `POST /control`。无查询参数。原始 UTF-8 正文为 `{"action":"status"}`、`{"action":"start"}` 或 `{"action":"stop"}`，且只允许这一个字段；客户端不能指定实例 ID。

请求头：

| Header | 格式 |
| --- | --- |
| x-device-id | SPKI DER 公钥的 SHA-256，小写 64 位 hex |
| x-timestamp | Unix UTC 秒，10 位数字 |
| x-nonce | SecureRandom 生成的 16 字节，小写 32 位 hex |
| x-signature | SHA256withECDSA 签名，ASN.1 DER 编码后标准 Base64 |

签名输入为以下八行，中间使用 LF，不添加最后一个换行：

```text
EC2SWITCH1
https://实际APIID.execute-api.ap-southeast-5.amazonaws.com
POST
/control
设备ID
时间戳
nonce
原始请求正文SHA256小写hex
```

前端 Vue 不接触私钥、不构造任意签名内容。原生插件仅允许上述三个动作和指定区域的默认 HTTPS API 域名。`.NET` 使用 `DSASignatureFormat.Rfc3279DerSequence` 验证，与 Java `SHA256withECDSA` 的 DER 签名匹配。

管理员从可信手机读取 SPKI 公钥和指纹，管理工具验证 P-256 曲线及指纹后，在 DynamoDB 条件写入：

```text
pk: DEVICE#<id>
publicKey: <SPKI DER Base64>
enabled: true
```

服务器先检查 ±120 秒时间偏差并强一致读取设备记录，再验签、验证动作，然后执行一个 DynamoDB 事务：检查设备仍为 enabled，同时用 `attribute_not_exists(pk)` 写入 `NONCE#<id>#<nonce>`。只会有一个相同 nonce 的请求被接受。记录的 expires 是服务器当前时间 +600 秒。最晚的合法签名窗口小于保留期，即使 TTL 延迟清理也不会使已用 nonce 被接受。

验证和事务完成后再查询真实 EC2 状态，并按状态调用启动或停止。不保存可能失真的“开/关”本地权威状态。命令返回的 pending/stopping 来自 AWS 响应。AWS 操作与 DynamoDB nonce 事务不是一个跨服务事务：函数崩溃或网络超时可能造成客户端无法确认操作结果，因此客户端只重试只读查询，不自动重发控制命令。

设备撤销通过管理端更新 enabled=false；事务检查会拒绝撤销后的新授权，但不能取消已经越过授权事务的在途操作。不存在 Lambda 公钥注册权限。注册管理员是可信边界，具有注册权限的人能够授权新设备控制本实例。

Keystore 私钥非导出不等同于“手机被完全攻破后绝对安全”。本方案不做远程 Key Attestation，也不限制只能注册一个设备。免登录、免指纹意味着手机的解锁能力和设备本身的安全性是重要边界。无控制接口密码重置、匿名注册、可导出私钥或浏览器降级私钥。

未实现跨请求业务锁；多人/多设备同时发送相反指令或在 AWS 控制台同时操作时，以 EC2 状态机为准。`IncorrectInstanceState` 转成 409，客户端刷新后再操作。无终止接口、终止 SDK 调用或 `ec2:TerminateInstances` IAM 授权。
