# Configuration Templates

Production configuration templates for Oluso deployments.

## Files

| Template | Description |
|----------|-------------|
| `appsettings.Production.json` | Public sample production config |

## Usage

1. Copy the template to your project:
   ```bash
   cp templates/appsettings.Production.json samples/Oluso.Sample/appsettings.Production.json
   ```

2. Update all placeholder values (marked with `YOUR_*` or `yourdomain.com`)

3. Set the environment to Production:
   ```bash
   export ASPNETCORE_ENVIRONMENT=Production
   ```

## Security Notes

- **Never commit** your actual `appsettings.Production.json` with real credentials
- The `.gitignore` excludes `appsettings*.json` files (except these templates)
- Use environment variables or Azure Key Vault for sensitive values in production
