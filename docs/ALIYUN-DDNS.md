# my.contoso1.asia 自动公网 IPv4 同步

已加入 Vue + .NET 工程。用户已确认 `contoso1.asia` 由阿里云云解析托管，且有 `my` 的 A 记录，提供的 RecordId 为 `2103041539206785024`，已设为模板默认值。**该 ID 尚未在云端核验，没有使用真实阿里云密钥；未部署、未修改任何线上 DNS 记录。**

## 工作过程

1. EC2 进入 running 时，EventBridge 触发独立 .NET Lambda。
2. Lambda 查询 `i-056494d14ab6b3dd0` 当时的实际状态和公网 IPv4，而不是使用事件里的旧数据。
3. 按配置的 RecordId 读取阿里云记录，核对主域名 `contoso1.asia`、主机名 `my`、类型 A、默认线路、记录已启用。
4. IP 改变时才调用 UpdateDomainRecord，保留记录原来的 TTL、线路与主机名，然后重新查询确认记录值。
5. 结果存入 DynamoDB；App 经原有设备认证接口显示公网 IP 和同步状态。关闭 App 不影响后台执行。
6. 每分钟再检查一次，用于等待公网 IP、弥补事件丢失和失败重试，也会纠正后来手动改错的同一条记录。需要手工维护此记录时，请先关闭自动同步功能。

只有该实例的 running 事件会直接触发，定时检查始终以该实例为目标。后台并发限制为 1，写入前后核对实例启动代次和 IP。由于 EC2 与阿里云不是一个事务，切换过程仍可能短暂出现旧值，下一次检查会纠正；不承诺跨云原子更新。

## 1. 获取 RecordId

打开 [DescribeDomainRecords 官方文档](https://help.aliyun.com/zh/dns/api-alidns-2015-01-09-describedomainrecords)，点击“调试”进入阿里云 OpenAPI Explorer，填写 DomainName=`contoso1.asia`。在返回结果中找 RR=`my`、Type=`A`、Line=`default` 的记录，复制它的 **RecordId**，不是 RequestId。

记录较多时可设置 RRKeyWord=`my`、TypeKeyWord=`A`、SearchMode=`COMBINATION`，并核对返回记录。RecordId 不是密钥。若同名有多个 A 记录，先确认 VPN 域名应当只指向目标实例；本功能只修改指定 ID，不会删掉其他 A 或 AAAA 记录。

## 2. 准备最小权限阿里云 RAM 用户

为这个任务创建专用 RAM 用户和 AccessKey；不要使用主账号密钥。运行时只需要：

- `alidns:DescribeDomainRecordInfo`
- `alidns:UpdateDomainRecord`

`tools/alidns-ram-policy.json` 提供域名范围的策略模板。将 REPLACE_ALIBABA_ACCOUNT_ID 改为你的阿里云账号 ID；将 REPLACE_THIS_ZONES_DOMAIN_ID 改为 **这个主域名** 的 DomainId（DescribeDomainRecordInfo 的返回值包含它）。当前官方文档对查询动作使用域名名称、更新动作列出 domainId；模板同时列出该域名名称及其确切 DomainId，以兼容这两种资源标识，不能填写其他域名或通配域名。

RAM 策略的资源范围是主域名；本程序另外用 RecordId 和字段核对，把实际修改限制到目标 A 记录。不要声称 RAM 已经限制到单条 DNS 记录。无需 AddDomainRecord、DeleteDomainRecord、SetDomainRecordStatus 或全域名管理权限。临时在 OpenAPI Explorer 列举记录用当前已登录管理账号，不要求长期运行密钥拥有列举权限。

## 3. 密钥仅存放在 AWS Secrets Manager

在 AWS 控制台切换到 **ap-southeast-5**，打开 Secrets Manager → 存储新密钥 → 其他类型密钥，录入两个键值：

```json
{
  "AccessKeyId": "你的专用阿里云RAM AccessKey ID",
  "AccessKeySecret": "对应的Secret"
}
```

选择默认的 AWS 托管密钥 `aws/secretsmanager`，名称可用 `ec2-switch/alidns`。保存并复制 Secret ARN。**不要把实际 AccessKey 发到聊天、写入源码或输入到手机里。** 本版本模板仅配置读取该 Secret 的权限；如果改用自定义 KMS 密钥，需另外针对该 KMS key 配置解密权限，不能直接照默认模板使用。

密钥读取发生在后台 Lambda 中，控制 API 没有读取该 Secret 的权限。每轮后台执行重新读取一次，使密钥轮换能在下一轮生效；日志只记通用错误类型，不输出 AccessKey、签名参数或原始响应。

## 4. 启用并部署

在现有工程根目录执行 `sam build`，然后：

```powershell
sam deploy --guided --stack-name personal-ec2-switch --region ap-southeast-5 --profile personal --capabilities CAPABILITY_IAM
```

交互参数填写：

| 参数 | 值 |
| --- | --- |
| InstanceId | i-056494d14ab6b3dd0 |
| EnableDdns | true |
| DnsZone | contoso1.asia |
| DnsRr | my |
| DnsRecordId | 2103041539206785024 |
| DnsSecretArn | 上面创建的 AWS Secret ARN |

模板默认 `EnableDdns=false`，所以未配置密钥时仍可部署原来的开关功能。开启后会增加独立的 Lambda、日志组、执行角色和 EventBridge 规则；这些服务及 Secrets Manager 可能产生费用。

后台 Lambda 的运行权限只允许读取 EC2 状态、读取指定 Secret、写入本实例 `SYNC#` 状态项；没有启停实例或注册设备的权限。网络使用 Lambda 默认公网出站，不需要把函数加入 VPC。

保留现有 App 的设备注册即可。重新构建并覆盖安装新版 APK（相同发布签名），才能显示新的 DNS 状态区域。App 仍然无登录、无指纹验证，开关操作不等待 DNS 更新完成。

## 5. EC2 和 VPN 前提

- 实例必须实际能获得自动分配的公网 IPv4。此代码只读取地址，不会修改网卡、子网、路由表，也不会自动解绑或释放已有 EIP。
- 如果当前绑定 EIP，必须先确认 EC2 公网地址配置再自行解绑/释放。**释放 EIP 可能无法找回**，本次没有执行。
- 在子网开启自动分配选项主要影响后续启动/创建时的分配，不能把它当作既有实例一定能获取公网 IP 的证明。配置后在控制台实际确认实例的 Public IPv4 address。
- 保持正确的互联网网关、路由、安全组和 VPN 端口设置。DDNS 不替代网络连通性配置。
- 若域名存在其他 A/AAAA、加权或非默认线路，VPN 客户端可能选中其他地址；本功能不会处理这些额外记录。
- 使用域名并保持 VPN 的服务器证书/公钥校验。DNS 修改不等于客户端立即重新解析，已有会话可能需断开重连；某些客户端需要重启隧道才能重新解析。

## 6. TTL、停机与费用

“已同步”只表示阿里云 API 中该记录值已经核对正确，**不代表所有递归 DNS 缓存已刷新，也不代表 VPN 服务已就绪**。当前版本保持原 TTL。若原 TTL 是 600 秒，客户端仍可能继续缓存旧地址；是否允许更低 TTL 取决于阿里云 DNS 套餐。降低 TTL 也不能立即清除之前已缓存的旧 TTL。

本版本按用户要求只在运行时更新 IP，**停机不删除、不暂停 A 记录**。EC2 自动公网 IP 被释放后，旧记录可能短暂或长期指向后来分配给其他人的地址，因此不要在停机时把旧 DNS 地址视为可信服务器；VPN 客户端必须校验服务器身份。如需停机自动暂停记录，可另外增加该行为和对应权限。

AWS 自动分配的公网 IPv4 也不是免费。官方公开标准价格为每 IP 每小时 **US$0.005**（实际抵扣与优惠以账户为准）。持续运行时，自动公网 IPv4 和 EIP 的地址费通常没有区别；主要节省来自停机后自动公网地址被释放，而保留的 EIP 仍计费。新加的 Secrets Manager、Lambda 等费用也应一起比较。[AWS VPC 定价](https://aws.amazon.com/vpc/pricing/)

## 验证情况

本次仅进行本机编译、离线 DNS 提供方模拟测试、前端构建及模板静态检查。没有访问你账户的 AWS / 阿里云 API，也没有查询或修改真实解析。上云后仍需验证 RAM 资源授权、Secret 读取、EventBridge 触发、EC2 自动公网地址及真实 DNS 更新。

参考：[EC2 状态事件](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/monitoring-instance-state-changes.html)、[阿里云修改记录](https://help.aliyun.com/zh/dns/api-alidns-2015-01-09-updatedomainrecord)、[阿里云读取记录](https://help.aliyun.com/zh/dns/api-alidns-2015-01-09-describedomainrecordinfo)。
