import sys

path = '.github/workflows/docker-publish.yml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('cache-to: type=gha,mode=max', 'cache-to: type=gha,mode=max\n          build-args: |\n            APP_VERSION_SUFFIX=${{ github.run_number }}')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)