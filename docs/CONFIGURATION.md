# 使用前需要配置什么

源码已设定的参数无需再次填写：

| 参数 | 当前值 |
| --- | --- |
| AWS 区域 | ap-southeast-5 |
| EC2 实例 ID | i-056494d14ab6b3dd0 |
| 阿里云主域名 | contoso1.asia |
| DNS 主机记录 | my |
| VPN 完整域名 | my.contoso1.asia |
| A 记录 RecordId | 2103041539206785024（用户提供，尚未云端核验） |
| Android applicationId | net.personal.ec2switch |

## APK 编译阶段

不需要任何 AWS / 阿里云密钥。需要本机 Android SDK 路径、Platform 35、Build-Tools 34.0.0（当前 Android Gradle Plugin 默认版本；SDK 许可已接受时可自动补齐）、Platform-Tools 和 JDK 21。Android Studio 已安装在 `D:\Program Files\Android\Android Studio`，其自带 `jbr` 当前为 Java 25；本工程 Gradle 8.11.1 应另行指定 JDK 21，不直接使用该 Java 25。依赖工具需要本机可写的缓存目录与联网下载权限。

## 后端部署阶段

1. 本机配置 AWS CLI 身份，例如 `personal` profile。只在你自己电脑登录或保存凭据，不要提供给 App。
2. 阿里云专用 RAM AccessKey：只授予本域名记录读取和修改权限，存入 **ap-southeast-5 的 AWS Secrets Manager**，JSON 键名为 `AccessKeyId` 和 `AccessKeySecret`。
3. 部署模板的 `DnsSecretArn`：填写上一步得到的 Secret ARN，不是密钥内容。
4. 部署时把 `EnableDdns` 从默认 `false` 改为 `true`，其余实例、域名及 RecordId 保留上述值。
5. RAM 策略模板中的阿里云账号 ID、当前域名 DomainId 需按真实值替换，详见 `ALIYUN-DDNS.md`。

## 手机首次打开

1. App 设置中的 **API 基础地址**：填写 AWS 部署输出 `ApiBaseUrl`。
2. 将手机生成的设备注册 JSON 保存到可信电脑。
3. 用 AWS 部署输出的 **RegistryTable** 运行 `tools/device.ps1` 完成一次公钥注册。

之后每次打开 App 无需登录或指纹。开机后等待旋转图标结束、显示“DNS 已更新成功”，即可打开 AnyConnect 尝试连接；DNS 缓存和 VPN 服务启动可能仍需短暂等待。

## AnyConnect

连接地址使用 `my.contoso1.asia`；如果 VPN 服务使用非默认端口，使用实际端口。AnyConnect 的服务器证书、VPN 账户认证和 EC2 上的 VPN 服务由现有 VPN 配置决定，本 App 不安装或配置 VPN 服务，也不保存 VPN 账号密码。

目前没有连接 AWS 或阿里云账户，没有部署后端、注册真实手机或更改真实 DNS。APK 已由本机脚本生成并通过签名验证，属于调试测试包；仅有 APK、没有上述后端与设备注册配置时不能控制实例。



