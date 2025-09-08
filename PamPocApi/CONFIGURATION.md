# Configuration Guide

## Configuration Sources (Priority Order)

1. **Command Line Arguments** (Highest priority)
2. **Environment Variables**
3. **User Secrets** (Development only)
4. **appsettings.{Environment}.json**
5. **appsettings.json** (Lowest priority)

## Environment Variables

Use the `PAMPOC__` prefix for all environment variables:

```bash
# Example environment variables
export PAMPOC__STT_URL="https://api.openai.com/v1/audio/transcriptions"
export PAMPOC__LLM_URL="https://api.openai.com/v1/chat/completions"
export PAMPOC__DEFAULT_LLM_MODEL="gpt-4"
export PAMPOC__TTS_MODE="http"
export PAMPOC__TTS_URL="https://api.elevenlabs.io/v1/text-to-speech"
```

## Configuration Files

### appsettings.json (Base configuration)
```json
{
  "ServiceConfiguration": {
    "SttUrl": "http://localhost:9000/v1/audio/transcriptions",
    "LlmUrl": "http://localhost:11434/v1/chat/completions",
    "DefaultLlmModel": "llama3.2",
    "TtsMode": "cli"
  }
}
```

### appsettings.Development.json (Development overrides)
- More verbose logging
- Local service URLs
- Development-friendly settings

### appsettings.Production.json (Production overrides)
- Reduced logging
- Production service URLs
- Security-focused settings

## User Secrets (Development)

For sensitive data during development:

```bash
dotnet user-secrets set "ServiceConfiguration:ApiKey" "your-secret-key"
```

## Docker Environment Variables

Use double underscores for nested configuration:

```dockerfile
ENV ServiceConfiguration__SttUrl=https://api.openai.com/v1/audio/transcriptions
ENV ServiceConfiguration__LlmUrl=https://api.openai.com/v1/chat/completions
```

## Kubernetes ConfigMaps and Secrets

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: pampoc-config
data:
  ServiceConfiguration__SttUrl: "https://api.openai.com/v1/audio/transcriptions"
  ServiceConfiguration__LlmUrl: "https://api.openai.com/v1/chat/completions"
```

## Best Practices

1. **Never commit secrets** to source control
2. **Use User Secrets** for development
3. **Use environment variables** for production
4. **Validate configuration** on startup
5. **Use typed configuration classes** instead of magic strings
6. **Prefix environment variables** to avoid conflicts