import importlib.util
import os
import unittest
from unittest.mock import patch
from pathlib import Path

spec = importlib.util.spec_from_file_location('deploy', Path(__file__).with_name('deploy.py'))
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


class DeploymentGuardTests(unittest.TestCase):
    def test_failed_stack_never_deployed_or_deleted(self):
        with patch.object(deploy, 'aws_json', return_value={'StackSummaries': [
            {'StackName': 'personal-ec2-switch', 'StackStatus': 'ROLLBACK_COMPLETE'}
        ]}), patch.object(deploy.subprocess, 'run') as run:
            with self.assertRaisesRegex(RuntimeError, 'ROLLBACK_COMPLETE'):
                deploy.main()
            run.assert_not_called()

    def test_read_failure_never_treated_as_new_stack(self):
        with patch.object(deploy, 'aws_json', side_effect=RuntimeError('AccessDenied')), \
                patch.object(deploy.subprocess, 'run') as run:
            with self.assertRaisesRegex(RuntimeError, 'AccessDenied'):
                deploy.main()
            run.assert_not_called()

    def test_wrong_region_secret_rejected_before_deploy(self):
        with patch.object(deploy, 'aws_json', side_effect=[
            {'StackSummaries': []}, {'AccountLimit': {'ConcurrentExecutions': 100}}
        ]), patch.dict(os.environ, {'DNS_SECRET_ARN': 'arn:aws:secretsmanager:us-east-1:bad'}), \
                patch.object(deploy.subprocess, 'run') as run:
            with self.assertRaises(ValueError):
                deploy.main()
            run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
