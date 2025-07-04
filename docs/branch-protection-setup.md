1# Branch Protection Setup Guide

This guide explains how to configure GitHub branch protection rules to ensure the test job passes before PRs can be merged.

## Required Branch Protection Rules

### For Master Branch (Production)

1. Go to **Settings** → **Branches** in your GitHub repository
2. Click **Add rule** for the `master` branch
3. Configure the following settings:

#### Required Settings
- ✅ **Require a pull request before merging**
  - ✅ Require approvals: `1`
  - ✅ Dismiss stale reviews when new commits are pushed
  - ✅ Require review from code owners (if you have CODEOWNERS file)

- ✅ **Require status checks to pass before merging**
  - ✅ Require branches to be up to date before merging
  - Add these required status checks:
    - `validate-naming-conventions`
    - `run-tests`
    - `validate-infrastructure / validate-infrastructure (TrialFinderV2, Development)`
    - `comment-naming-examples`

- ✅ **Require conversation resolution before merging**
- ✅ **Restrict pushes that create files larger than 100MB**

#### Optional Settings (Recommended)
- ✅ **Include administrators** (enforce rules for repository admins)
- ✅ **Allow force pushes** → ❌ **Disabled** (recommended)
- ✅ **Allow deletions** → ❌ **Disabled** (recommended)

### For Develop Branch (Development)

1. Add another rule for the `develop` branch
2. Use similar settings as master, but with relaxed approval requirements:

#### Required Settings
- ✅ **Require a pull request before merging**
  - ✅ Require approvals: `1` (can be 0 for development)
  - ✅ Dismiss stale reviews when new commits are pushed

- ✅ **Require status checks to pass before merging**
  - ✅ Require branches to be up to date before merging
  - Add these required status checks:
    - `validate-naming-conventions`
    - `run-tests` ← **This is the critical one for test coverage**
    - `comment-naming-examples`

## Finding Status Check Names

To find the exact status check names:

1. Create a test PR
2. Wait for the workflow to run
3. Go to the PR's **Checks** tab
4. Note the exact names of each check (they appear on the left sidebar)
5. Use these exact names in the branch protection rules

## Test Coverage Enforcement

The `run-tests` job will:
- ✅ Run all unit tests with coverage collection
- ✅ Run all integration tests
- ✅ Generate coverage reports
- ✅ **Fail if unit test coverage < 80%**
- ✅ Post coverage report as PR comment
- ✅ Upload coverage artifacts

## Workflow Job Dependencies

The workflow is structured so that:
```
validate-naming-conventions (independent)
run-tests (independent)
├── validate-infrastructure (depends on both)
└── comment-naming-examples (depends on both)
```

This means:
- Tests must pass before infrastructure validation runs
- All jobs must pass before PR can be merged (when branch protection is enabled)
- Coverage threshold is enforced at the test level

## Verifying Setup

After configuring branch protection:

1. Create a test PR that would fail coverage (reduce test coverage)
2. Verify the PR cannot be merged due to failing `run-tests` check
3. Fix the coverage issue
4. Verify the PR can now be merged

## Example Branch Protection Configuration

Here's a screenshot of what your branch protection rules should look like:

### Master Branch Protection
```
Branch name pattern: master
☑ Require a pull request before merging
  ☑ Require approvals (1)
  ☑ Dismiss stale reviews
☑ Require status checks to pass before merging
  ☑ Require branches to be up to date
  Required status checks:
    - validate-naming-conventions
    - run-tests
    - validate-infrastructure / validate-infrastructure (TrialFinderV2, Development) 
    - comment-naming-examples
☑ Require conversation resolution before merging
☑ Include administrators
```

### Develop Branch Protection
```
Branch name pattern: develop
☑ Require a pull request before merging
  ☑ Require approvals (1)
☑ Require status checks to pass before merging
  ☑ Require branches to be up to date
  Required status checks:
    - validate-naming-conventions
    - run-tests
    - comment-naming-examples
```

## Testing the Protection

1. **Create a test PR** that reduces test coverage below 80%
2. **Verify PR is blocked** with status "Some checks haven't completed yet"
3. **Check that merge button is disabled** with message about required status checks
4. **Fix the coverage** and verify PR becomes mergeable

## Troubleshooting

### Status Checks Not Appearing
- Ensure you've pushed at least one commit that triggers the workflow
- Check that workflow has run at least once on the target branch
- Refresh the branch protection settings page

### Wrong Status Check Names
- Check the exact names in the PR's Checks tab
- Status check names are case-sensitive
- Include the full path for matrix jobs (e.g., `job-name / matrix-name`)

### Tests Not Required
- Verify `run-tests` is listed in required status checks
- Ensure branch protection rule is enabled
- Check that the rule applies to the correct branch