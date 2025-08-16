# CDK Native Optimization Migration Plan

## Overview
This document outlines the migration from the current sequential deployment approach to the optimized CDK native deployment strategy.

## Current State (deploy-dev.yml)
- **Build Time**: ~5 minutes (repeated 9 times)
- **Deployment Time**: ~20-25 minutes
- **Total Time**: ~25-30 minutes
- **Approach**: Sequential job-per-stack deployment with matrix strategy

## Target State (deploy-dev-optimized.yml)
- **Build Time**: ~5 minutes (once)
- **Deployment Time**: ~10-15 minutes (full) or ~5 minutes (no changes)
- **Total Time**: ~15-20 minutes (full) or ~10 minutes (no changes)
- **Approach**: CDK native parallel deployment with change detection

## Migration Steps

### Phase 1: Testing (Current)
1. ✅ Created `deploy-dev-optimized.yml` with CDK native approach
2. ⏳ Test in feature branch with limited scope
3. ⏳ Monitor performance metrics and validate outputs
4. ⏳ Verify change detection works correctly

### Phase 2: Parallel Run
1. Run both workflows in parallel for 1-2 weeks
2. Compare outputs and deployment times
3. Validate all resources are created correctly
4. Monitor for any deployment failures or issues

### Phase 3: Gradual Migration
1. Update `deploy-dev.yml` to use optimized approach for one application
2. Monitor for 1 week
3. If successful, migrate remaining applications
4. Keep old job definitions commented for rollback

### Phase 4: Full Migration
1. Replace `deploy-dev.yml` with optimized version
2. Archive old workflow as `deploy-dev-legacy.yml`
3. Update documentation and team training
4. Apply same optimization to `deploy-prod.yml`

## Key Differences

### 1. Deployment Strategy
**Old**: Sequential jobs with matrix strategy
```yaml
strategy:
  matrix:
    app: [TrialFinderV2, TrialMatch]
```

**New**: Consolidated deployment with pattern matching
```yaml
cdk deploy "dev-tfv2-*" --concurrency 3 --exclusively
```

### 2. Change Detection
**Old**: Always deploys all stacks
**New**: Uses `--exclusively` to skip unchanged stacks

### 3. Build Redundancy
**Old**: 9 separate build steps
**New**: 3 build steps (once per major job)

### 4. Output Management
**Old**: Individual output files per stack
**New**: Consolidated output files per application

## Performance Metrics

### Expected Improvements
| Metric | Current | Optimized | Improvement |
|--------|---------|-----------|-------------|
| Full Deployment | 25-30 min | 15-20 min | 40-50% faster |
| No Changes | 25-30 min | 5-10 min | 80% faster |
| Build Steps | 9 | 3 | 67% reduction |
| API Calls | High | Low | Reduced throttling |

### Monitoring Points
1. CloudFormation stack creation/update times
2. GitHub Actions workflow duration
3. CDK deployment logs for skipped stacks
4. Error rates and retry counts

## Risk Mitigation

### Potential Risks
1. **Parallel deployment conflicts**: Mitigated by CDK's dependency resolution
2. **Change detection false negatives**: Monitor first deployments carefully
3. **Output file compatibility**: Validate downstream consumers

### Rollback Plan
1. Keep `deploy-dev-legacy.yml` for quick rollback
2. Feature flag in workflow to toggle optimization
3. Ability to force full deployment with workflow dispatch

## Validation Checklist

### Pre-Migration
- [ ] Test optimized workflow in feature branch
- [ ] Validate all stacks deploy correctly
- [ ] Verify change detection works
- [ ] Confirm output files are generated
- [ ] Test with no changes scenario
- [ ] Test with partial changes scenario

### During Migration
- [ ] Monitor deployment times
- [ ] Check CloudFormation events
- [ ] Validate resource creation
- [ ] Verify IAM permissions
- [ ] Test application functionality

### Post-Migration
- [ ] Document new workflow behavior
- [ ] Update runbooks
- [ ] Train team on new approach
- [ ] Monitor for 2 weeks
- [ ] Apply to production workflow

## Next Steps

1. **Immediate**: Test `deploy-dev-optimized.yml` in feature branch
2. **Week 1**: Parallel run with monitoring
3. **Week 2**: Gradual migration if metrics are positive
4. **Week 3**: Full migration and documentation
5. **Week 4**: Apply optimization to production workflow

## Success Criteria

The migration is considered successful when:
1. ✅ Deployment time reduced by at least 40% for full deployments
2. ✅ Deployment time reduced by at least 70% when no changes present
3. ✅ All stacks deploy successfully with correct resources
4. ✅ Change detection accurately identifies modified stacks
5. ✅ No increase in deployment failure rate
6. ✅ Output files maintain compatibility with downstream processes

## Commands for Testing

```bash
# Test optimized workflow in feature branch
gh workflow run deploy-dev-optimized.yml --ref feature/healthlake

# Monitor deployment
gh run watch

# Compare with standard workflow
gh workflow run deploy-dev.yml --ref feature/healthlake

# Check deployment times
gh run list --workflow=deploy-dev-optimized.yml --limit=5
```

## References
- [CDK Deploy Options](https://docs.aws.amazon.com/cdk/v2/guide/cli.html#cli-deploy)
- [GitHub Actions Optimization](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions)
- [CloudFormation Best Practices](https://docs.aws.amazon.com/AWSCloudFormation/latest/UserGuide/best-practices.html)