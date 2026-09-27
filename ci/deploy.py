"""Deploy the approved build artifact; never delete failed stacks automatically."""
import json
import os
import subprocess


def aws_json(*args):
    result = subprocess.run(
        ['aws', *args, '--region', 'ap-southeast-5', '--output', 'json', '--no-cli-pager'],
        check=True, capture_output=True, text=True)
    return json.loads(result.stdout)


def main():
    # List avoids treating an access-denied DescribeStacks as a nonexistent stack.
    stack = 'personal-ec2-switch'
    stacks = aws_json('cloudformation', 'list-stacks')['StackSummaries']
    for item in stacks:
        if item['StackName'] == stack and item['StackStatus'] != 'DELETE_COMPLETE':
            if item['StackStatus'] not in ('CREATE_COMPLETE', 'UPDATE_COMPLETE', 'UPDATE_ROLLBACK_COMPLETE'):
                raise RuntimeError(f"Stack is {item['StackStatus']}; resolve it in CloudFormation before approving deployment. No stack deleted.")
    # The effective minimum reserve can vary by account. AWS remains authoritative.
    limits = aws_json('lambda', 'get-account-settings')['AccountLimit']
    print('Current Lambda concurrency:', limits.get('ConcurrentExecutions'),
          'unreserved:', limits.get('UnreservedConcurrentExecutions'))
    secret_arn = os.environ['DNS_SECRET_ARN']
    if not secret_arn.startswith('arn:aws:secretsmanager:ap-southeast-5:'):
        raise ValueError('DNS_SECRET_ARN must be a Malaysia Secrets Manager ARN')
    subprocess.run([
        'sam', 'deploy', '--template-file', 'bundle/backend/template.yaml',
        '--stack-name', stack, '--region', 'ap-southeast-5',
        '--s3-bucket', os.environ['PACKAGE_BUCKET'], '--s3-prefix', 'lambda',
        '--capabilities', 'CAPABILITY_IAM', '--no-confirm-changeset',
        '--no-fail-on-empty-changeset', '--parameter-overrides',
        'InstanceId=i-056494d14ab6b3dd0', 'EnableDdns=true',
        'DnsZone=contoso1.asia', 'DnsRr=my', 'DnsRecordId=2103041539206785024',
        f'DnsSecretArn={secret_arn}'
    ], check=True)
    output = aws_json('cloudformation', 'describe-stacks', '--stack-name', stack)
    with open('deployment-outputs.json', 'w', encoding='utf-8') as handle:
        json.dump(output['Stacks'][0].get('Outputs', []), handle, indent=2)


if __name__ == '__main__':
    main()
