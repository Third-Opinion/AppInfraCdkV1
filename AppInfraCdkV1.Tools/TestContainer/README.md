# Build
docker build -t thirdopinion/infra/deploy-placeholder .

# Test locally
docker run -p 80:80 -p 8080:8080 thirdopinion/infra/deploy-placeholder

# In another terminal, test both ports:
curl http://localhost/
curl http://localhost/health
curl http://localhost:8080/
curl http://localhost:8080/health

# Deploy to AWS

aws ecr get-login-password --region us-east-2 | docker login --username AWS --password-stdin 615299752206.dkr.ecr.us-east-2.amazonaws.com

docker tag thirdopinion/infra/deploy-placeholder:latest 615299752206.dkr.ecr.us-east-2.amazonaws.com/thirdopinion/infra/deploy-placeholder:latest

docker push 615299752206.dkr.ecr.us-east-2.amazonaws.com/thirdopinion/infra/deploy-placeholder:latest