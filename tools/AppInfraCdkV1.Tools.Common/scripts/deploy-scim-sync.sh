#!/bin/bash

# =================================================================
# DEPRECATED: SCIM Sync Deployment Script
# =================================================================
#
# This script has been replaced by the integrated CDK deployment.
# 
# The SCIM Sync stack is now deployed through the main CDK program,
# and configuration is handled by a separate script.
#
# =================================================================

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${YELLOW}[DEPRECATED]${NC} This script has been replaced!"
echo ""
echo -e "${BLUE}New deployment process:${NC}"
echo ""
echo "1. Deploy the stack via CDK:"
echo "   cd AppInfraCdkV1.Deploy"
echo "   dotnet run -- --app=ScimSync --environment=Development"
echo "   AWS_PROFILE=to-dev-admin cdk deploy"
echo ""
echo "2. Configure SSM parameters:"
echo "   ./configure-scim-sync.sh dev configure"
echo ""
echo "3. Test the deployment:"
echo "   ./configure-scim-sync.sh dev test"
echo ""
echo -e "${GREEN}Please use the new process above.${NC}"
echo ""
echo "For detailed instructions, see: docs/scim-sync-deployment.md"
echo ""

# If user provided arguments, suggest the new script
if [ $# -gt 0 ]; then
    SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
    echo -e "${BLUE}Redirecting to new script...${NC}"
    echo ""
    exec "$SCRIPT_DIR/configure-scim-sync.sh" "$@"
fi

exit 0