# 当前 CloudFormation AccessDenied 的修复

## 完整部署权限：推荐网页配置

已新增三份客户托管策略 JSON：

- tools/Ec2SwitchDeployCloudFormation.json
- tools/Ec2SwitchDeployResources.json
- tools/Ec2SwitchDeployRoles.json

以有 IAM 授权权限的管理员身份进入 IAM → 策略 → 创建策略 → JSON，分别粘贴三个文件内容，策略名称采用文件名去掉 .json。随后在 IAM → 用户 → iamanson → 权限 → 添加权限 → 直接附加策略中选择三份策略并保存。不要把完整策略作为用户内联策略添加，以免触发内联策略总大小限制。此前的 CloudFormation 内联补丁可以保留；它与新的 CloudFormation 策略重复。

这些策略固定账号 492213594399、区域 ap-southeast-5、栈名 personal-ec2-switch。已覆盖 SAM 辅助栈、S3 上传及其默认 KMS 加密、两个 Lambda、DynamoDB、日志、EventBridge 和两个运行角色的部署操作；设备注册只允许表内 DEVICE# 前缀。实际 AWS 部署尚未验证，组织 SCP、权限边界或显式 Deny 仍可能阻止部署。

权限边界说明：S3 限定 SAM 辅助桶名称前缀；多数服务限定项目资源名称前缀。API Gateway 创建限定 API 名，但其后续资源 ID 尚未生成，因此管理权限覆盖本区域 /apis/* 和 /tags/*，并非只覆盖这个 API。部署完成后可按实际 API ID 收紧。IAM 策略允许编辑本项目两个运行角色的内联策略，并将这些角色传给 Lambda；这是有较高信任要求的部署权限，不应授予普通 App 设备或不可信用户，也不等于防止权限提升的 permissions boundary。部署策略与模板内 Lambda 的最小运行权限是两回事。模板更新者应是可信管理员。

JSON 解析、客户托管策略大小检查和 PowerShell 脚本语法及无写入预览已通过；未执行 AWS IAM 策略模拟、创建或附加权限。脚本方案见 tools/grant-deploy-permissions.ps1：需管理员 CLI profile，默认仅预览，加 -Apply 才写入；不会覆盖内容不同的已有策略。网页方式不需要该脚本。

## 先前单项错误补丁

`tools/deploy-cloudformation-policy.json` 是针对当前账号 492213594399、区域 ap-southeast-5、部署栈 personal-ec2-switch 和 SAM 辅助栈 aws-sam-cli-managed-default 的 CloudFormation 权限补丁。它不是完整部署权限策略，也不是 Lambda 运行角色策略。

使用具有 IAM 用户授权权限的管理员身份，在 IAM → 用户 → iamanson → 权限 → 添加权限 → 创建内联策略 → JSON 中粘贴文件内容，策略名可用 Ec2SwitchCloudFormationDeploy。不要创建新 AccessKey，也不要给 Lambda 附加此策略。

然后在项目根目录重试 `./tools/deploy.ps1 -Profile personal`。该补丁解决当前明确报出的 CloudFormation 权限缺失；未进行云端策略模拟或部署验证。

SAM 会先创建辅助 S3 存储，再通过 CloudFormation 创建模板中的 Lambda、HTTP API Gateway、DynamoDB、CloudWatch Logs、EventBridge 规则以及 IAM 运行角色。部署身份还必须具有这些资源的创建、读取、更新及回滚权限，以及限定到运行角色的 iam:PassRole。不能把“身份查询成功”或“CloudFormation 权限已添加”当作完整部署授权已验证。请由管理员检查现有用户/组策略补足；无需为了此错误授予整个账号的 AdministratorAccess。

如果 IAM 用户没有添加策略的权限，须由账号管理员执行上述授权。只需共享错误信息或策略名称，不要共享 AccessKey 或 Secret 内容。
