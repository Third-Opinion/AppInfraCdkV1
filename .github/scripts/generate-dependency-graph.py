#!/usr/bin/env python3
"""
Generate Stack Dependency Visualization for AppInfraCdkV1

This script visualizes the dependency tree for all CDK stacks in the project,
showing the deployment order and relationships between different applications.
"""

import json
import sys
import os

def print_dependency_tree():
    print("📊 Complete Stack Dependency Tree (from deployment):")
    print("")
    
    # Base Infrastructure (always independent)
    print("🏗️ Base Infrastructure")
    print("└── dev-shared-stack-ue2 (no dependencies)")
    print("")
    
    # TrialFinderV2 - correct dependency pattern
    print("🚀 TrialFinderV2")
    print("├── dev-tfv2-alb-ue2 → depends on: [dev-shared-stack-ue2]")
    print("├── dev-tfv2-cognito-ue2 → depends on: [dev-shared-stack-ue2]") 
    print("├── dev-tfv2-data-ue2 → depends on: [dev-shared-stack-ue2]")
    print("└── dev-tfv2-ecs-ue2 → depends on: [dev-tfv2-alb-ue2, dev-tfv2-data-ue2]")
    print("")
    
    # TrialMatch - correct dependency pattern
    print("🔬 TrialMatch")
    print("├── dev-tm-alb-ue2 → depends on: [dev-shared-stack-ue2]")
    print("├── dev-tm-cognito-ue2 → depends on: [dev-shared-stack-ue2]")
    print("├── dev-tm-data-ue2 → depends on: [dev-shared-stack-ue2]")
    print("└── dev-tm-ecs-ue2 → depends on: [dev-tm-alb-ue2, dev-tm-data-ue2]")
    print("")
    
    # LakeFormation - sequential dependencies
    print("🗄️ LakeFormation")
    print("├── dev-lf-storage-ue2 (no dependencies)")
    print("├── dev-lf-setup-ue2 → depends on: [dev-lf-storage-ue2]")
    print("└── dev-lf-permissions-ue2 → depends on: [dev-lf-setup-ue2]")
    print("")
    
    print("🔄 Actual Deployment Strategy Used:")
    print("• Base Infrastructure: Deployed first (standalone)")
    print("• Regular Apps: Deployed with --concurrency 2 --exclusively")
    print("  - CDK automatically respects dependencies")
    print("  - Parallel deployment where dependencies allow")
    print("  - Skips unchanged stacks automatically")
    print("• LakeFormation: Deployed sequentially per dependencies")

if __name__ == "__main__":
    print_dependency_tree()