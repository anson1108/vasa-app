// Regenerate templates from the reviewed deployment policies. No AWS calls.
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const ref = name => ({Ref:name});
const sub = value => ({'Fn::Sub':value});
const att = (name,key='Arn') => ({'Fn::GetAtt':[name,key]});
const allow = (Action,Resource,Condition) => ({Effect:'Allow',Action,Resource,...(Condition?{Condition}:{})});
const policy = Statement => ({Version:'2012-10-17',Statement});
const prefix = 'vasa-app';
const bucketName = 'vasa-app-packages-492213594399-ap-southeast-5';
const trust = (service,source) => policy([{Effect:'Allow',Principal:{Service:service},Action:'sts:AssumeRole',Condition:{StringEquals:{'aws:SourceAccount':ref('AWS::AccountId')},ArnEquals:{'aws:SourceArn':sub(source)}}}]);
const bucket = {Type:'AWS::S3::Bucket',DeletionPolicy:'Retain',UpdateReplacePolicy:'Retain',Properties:{VersioningConfiguration:{Status:'Enabled'},BucketEncryption:{ServerSideEncryptionConfiguration:[{ServerSideEncryptionByDefault:{SSEAlgorithm:'AES256'}}]},PublicAccessBlockConfiguration:{BlockPublicAcls:true,BlockPublicPolicy:true,IgnorePublicAcls:true,RestrictPublicBuckets:true}}};
const bucketPolicy = (logical) => ({Type:'AWS::S3::BucketPolicy',Properties:{Bucket:ref(logical),PolicyDocument:policy([{Effect:'Deny',Principal:'*',Action:'s3:*',Resource:[att(logical,'Arn'),sub('${'+logical+'.Arn}/*')],Condition:{Bool:{'aws:SecureTransport':'false'}}}])}});
const packageTemplate={AWSTemplateFormatVersion:'2010-09-09',Description:'Malaysia package bucket for vasa-app Lambda deployments',Resources:{PackageBucket:{...bucket,Properties:{...bucket.Properties,BucketName:bucketName}},PackageBucketPolicy:bucketPolicy('PackageBucket')},Outputs:{PackageBucket:{Value:ref('PackageBucket')}}};
const resources={ArtifactBucket:bucket,ArtifactBucketPolicy:bucketPolicy('ArtifactBucket')};
const artifactPermissions=allow(['s3:GetObject','s3:GetObjectVersion','s3:PutObject','s3:GetBucketLocation','s3:GetBucketVersioning','s3:GetBucketAcl','s3:ListBucket'],[att('ArtifactBucket'),sub('${ArtifactBucket.Arn}/*')]);
const artifactKms=allow(['kms:Decrypt','kms:GenerateDataKey'],[sub('arn:${AWS::Partition}:kms:${AWS::Region}:${AWS::AccountId}:key/*')],{StringEquals:{'kms:ViaService':sub('s3.${AWS::Region}.amazonaws.com'),'kms:CallerAccount':ref('AWS::AccountId')},StringLike:{'kms:EncryptionContext:aws:s3:arn':sub('${ArtifactBucket.Arn}/*')}});
const logs=name=>allow(['logs:CreateLogStream','logs:PutLogEvents'],[sub('arn:${AWS::Partition}:logs:${AWS::Region}:${AWS::AccountId}:log-group:/aws/codebuild/'+name+':*')]);
for(const name of ['Build','Deploy']){
 const projectName=prefix+'-'+name.toLowerCase();
 resources[name+'Log']={Type:'AWS::Logs::LogGroup',Properties:{LogGroupName:'/aws/codebuild/'+projectName,RetentionInDays:14}};
 resources[name+'Role']={Type:'AWS::IAM::Role',Properties:{AssumeRolePolicyDocument:trust('codebuild.amazonaws.com','arn:${AWS::Partition}:codebuild:${AWS::Region}:${AWS::AccountId}:project/'+projectName),Policies:[{PolicyName:'BuildArtifactsAndLogs',PolicyDocument:policy([artifactPermissions,artifactKms,logs(projectName)])}]}};
 resources[name+'Project']={Type:'AWS::CodeBuild::Project',Properties:{Name:projectName,ServiceRole:att(name+'Role'),TimeoutInMinutes:30,ConcurrentBuildLimit:1,Artifacts:{Type:'CODEPIPELINE'},Source:{Type:'CODEPIPELINE',BuildSpec:'ci/'+name.toLowerCase()+'.yml'},Environment:{Type:'LINUX_CONTAINER',ComputeType:'BUILD_GENERAL1_SMALL',Image:'aws/codebuild/standard:7.0',PrivilegedMode:false,EnvironmentVariables:name==='Deploy'?[{Name:'DNS_SECRET_ARN',Type:'PLAINTEXT',Value:ref('DnsSecretArn')},{Name:'PACKAGE_BUCKET',Type:'PLAINTEXT',Value:bucketName}]:[]},LogsConfig:{CloudWatchLogs:{Status:'ENABLED',GroupName:ref(name+'Log')}},EncryptionKey:'alias/aws/s3'}};
}
// Reuse the application's scoped deployment permissions. No static AWS credentials.
for(const [i,file] of ['Ec2SwitchDeployCloudFormation','Ec2SwitchDeployResources','Ec2SwitchDeployRoles'].entries()){
 let doc=JSON.parse(fs.readFileSync(path.join(root,'tools',file+'.json'),'utf8'));
 if(i===1) doc.Statement=doc.Statement.filter(s=>!['SamArtifactBuckets','SamArtifactObjects','SamS3ManagedEncryption','RegisterDevices'].includes(s.Sid));
 resources['DeployPolicy'+i]={Type:'AWS::IAM::ManagedPolicy',Properties:{PolicyDocument:doc,Roles:[ref('DeployRole')]}};
}
resources.DeployRole.Properties.Policies.push({PolicyName:'DeploymentChecksAndPackages',PolicyDocument:policy([
 allow(['cloudformation:ListStacks','lambda:GetAccountSettings'],'*',{StringEquals:{'aws:RequestedRegion':'ap-southeast-5'}}),
 allow(['s3:GetBucketLocation','s3:ListBucket'],['arn:aws:s3:::'+bucketName]),
 allow(['s3:GetObject','s3:GetObjectVersion','s3:PutObject','s3:AbortMultipartUpload'],['arn:aws:s3:::'+bucketName+'/*'])
])});
resources.PipelineRole={Type:'AWS::IAM::Role',Properties:{AssumeRolePolicyDocument:trust('codepipeline.amazonaws.com','arn:${AWS::Partition}:codepipeline:${AWS::Region}:${AWS::AccountId}:vasa-app'),Policies:[{PolicyName:'PipelineStages',PolicyDocument:policy([artifactPermissions,artifactKms,allow(['codeconnections:UseConnection','codestar-connections:UseConnection'],ref('ConnectionArn')),allow(['codebuild:StartBuild','codebuild:BatchGetBuilds'],[att('BuildProject'),att('DeployProject')])])}]}};
const action=(Name,Category,Provider,Configuration,InputArtifacts,OutputArtifacts)=>({Name,ActionTypeId:{Category,Owner:'AWS',Provider,Version:'1'},Configuration,...(InputArtifacts?{InputArtifacts:InputArtifacts.map(Name=>({Name}))}:{}),...(OutputArtifacts?{OutputArtifacts:OutputArtifacts.map(Name=>({Name}))}:{}),RunOrder:1});
resources.Pipeline={Type:'AWS::CodePipeline::Pipeline',DependsOn:['ArtifactBucketPolicy','DeployPolicy0','DeployPolicy1','DeployPolicy2'],Properties:{Name:'vasa-app',PipelineType:'V2',ExecutionMode:'QUEUED',RoleArn:att('PipelineRole'),ArtifactStore:{Type:'S3',Location:ref('ArtifactBucket')},Stages:[
 {Name:'Source',Actions:[action('GitHub','Source','CodeStarSourceConnection',{ConnectionArn:ref('ConnectionArn'),FullRepositoryId:'anson1108/vasa-app',BranchName:'main',DetectChanges:'true',OutputArtifactFormat:'CODE_ZIP'},null,['Source'])]},
 {Name:'BuildAndTest',Actions:[action('Build','Build','CodeBuild',{ProjectName:ref('BuildProject')},['Source'],['Build'])]},
 {Name:'ApproveDeployment',Actions:[action('Approve','Approval','Manual',{CustomData:'Confirm Lambda quota is approved and personal-ec2-switch is deployable. Approving enables EC2 control API and DNS synchronization; a running EC2 may cause an A-record update.'})]},
 {Name:'DeployMalaysia',Actions:[action('Deploy','Build','CodeBuild',{ProjectName:ref('DeployProject')},['Build'],['Deployment'])]}
]}};
const template={AWSTemplateFormatVersion:'2010-09-09',Description:'GitHub main -> tests -> manual approval -> SAM deployment in Malaysia',Parameters:{ConnectionArn:{Type:'String',AllowedPattern:'^arn:aws:(codeconnections|codestar-connections):ap-southeast-1:492213594399:connection/.+$'},DnsSecretArn:{Type:'String',Default:'arn:aws:secretsmanager:ap-southeast-5:492213594399:secret:aliyundns-VvNFXf',AllowedPattern:'^arn:aws:secretsmanager:ap-southeast-5:492213594399:secret:.+$'}},Resources:resources,Outputs:{PipelineName:{Value:ref('Pipeline')},ArtifactBucket:{Value:ref('ArtifactBucket')},BuildProject:{Value:ref('BuildProject')},DeployProject:{Value:ref('DeployProject')}}};
fs.writeFileSync(path.join(__dirname,'packages.template.json'),JSON.stringify(packageTemplate,null,2)+'\n');
fs.writeFileSync(path.join(__dirname,'pipeline.template.json'),JSON.stringify(template,null,2)+'\n');
console.log('Generated pipeline and Malaysia package bucket templates.');

