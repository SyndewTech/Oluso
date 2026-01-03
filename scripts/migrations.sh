#!/bin/bash
# Oluso EF Core Migration Helper
# Usage: ./scripts/migrations.sh <command> [context] <provider> [migration-name]
#
# Uses provider-specific DbContext types per Microsoft's recommended approach:
# https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers

set -e

# Get context configuration (public contexts only)
# Returns: project|startup|base-context-name
get_context_config() {
    local ctx="$1"
    local ctx_lower=$(echo "$ctx" | tr '[:upper:]' '[:lower:]')

    case "$ctx_lower" in
        oluso|olusodb|main)
            echo "src/backend/Oluso.EntityFramework|samples/Oluso.Sample|OlusoDbContext"
            ;;
        scim)
            echo "src/backend/Oluso.Enterprise/Scim|samples/Oluso.Sample|ScimDbContext"
            ;;
        ldap)
            echo "src/backend/Oluso.Enterprise/Ldap|samples/Oluso.Sample|LdapDbContext"
            ;;
        saml)
            echo "src/backend/Oluso.Enterprise/Saml|samples/Oluso.Sample|SamlDbContext"
            ;;
        *)
            echo ""
            ;;
    esac
}

# Parse context name from input
get_context_name() {
    local ctx="$1"
    local ctx_lower=$(echo "$ctx" | tr '[:upper:]' '[:lower:]')

    case "$ctx_lower" in
        oluso|olusodb|main) echo "oluso" ;;
        scim) echo "scim" ;;
        ldap) echo "ldap" ;;
        saml) echo "saml" ;;
        *) echo "" ;;
    esac
}

# Normalize provider name
get_provider() {
    local prov="$1"
    local prov_lower=$(echo "$prov" | tr '[:upper:]' '[:lower:]')

    case "$prov_lower" in
        sqlite|sql)      echo "Sqlite" ;;
        sqlserver|mssql) echo "SqlServer" ;;
        postgres|pg|pgsql|postgresql) echo "Postgres" ;;
        *)               echo "" ;;
    esac
}

# Get the provider-specific context name
# e.g., OlusoDbContext + Sqlite = OlusoDbContextSqlite
get_provider_context() {
    local base_ctx="$1"
    local provider="$2"
    echo "${base_ctx}${provider}"
}

get_output_dir() {
    local prov="$1"
    case "$prov" in
        Sqlite)    echo "Migrations/Sqlite" ;;
        SqlServer) echo "Migrations/SqlServer" ;;
        Postgres)  echo "Migrations/Postgres" ;;
    esac
}

command="${1:-help}"

# Determine if second arg is context or provider
context_name=""
provider=""
migration_name=""

if [ "$command" = "help" ] || [ -z "$2" ]; then
    context_name="oluso"
else
    # Try to parse as context first
    ctx=$(get_context_name "$2")
    if [ -n "$ctx" ]; then
        context_name="$ctx"
        provider=$(get_provider "${3:-Sqlite}")
        migration_name="${4:-}"
    else
        # Not a context, treat as provider
        context_name="oluso"
        provider=$(get_provider "$2")
        migration_name="${3:-}"
    fi
fi

# Get context configuration
config=$(get_context_config "$context_name")

if [ -z "$config" ]; then
    echo "Unknown context: $context_name"
    echo "Valid contexts: oluso, scim, ldap, saml"
    echo ""
    echo "For private contexts (billing, workflows), use:"
    echo "  ./private/scripts/migrations.sh"
    exit 1
fi

IFS='|' read -r PROJECT STARTUP BASE_CONTEXT <<< "$config"

if [ -z "$provider" ] && [ "$command" != "help" ]; then
    provider="Sqlite"
fi

if [ -n "$provider" ] && [ -z "$(get_provider "$provider")" ]; then
    echo "Unknown provider: $provider"
    echo "Supported: Sqlite, SqlServer, Postgres"
    exit 1
fi

# Get provider-specific context name (e.g., OlusoDbContextSqlite)
CONTEXT=$(get_provider_context "$BASE_CONTEXT" "$provider")
output_dir=$(get_output_dir "$provider")

case "$command" in
    add)
        if [ -z "$migration_name" ]; then
            echo "Usage: $0 add [context] <provider> <migration-name>"
            echo "       $0 add <provider> <migration-name>  (uses oluso context)"
            exit 1
        fi

        echo "[$context_name] Adding migration '$migration_name' for $provider..."
        echo "  Context: $CONTEXT"
        echo "  Output:  $output_dir"
        dotnet ef migrations add "$migration_name" \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP" \
            --output-dir "$output_dir"
        ;;

    remove)
        echo "[$context_name] Removing last migration for $provider..."
        dotnet ef migrations remove \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP"
        ;;

    list)
        echo "[$context_name] Listing migrations for $provider..."
        dotnet ef migrations list \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP"
        ;;

    script)
        output_file="migrations-${context_name}-$(echo "$provider" | tr '[:upper:]' '[:lower:]').sql"
        echo "[$context_name] Generating SQL script for $provider -> $output_file"
        dotnet ef migrations script --idempotent \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP" \
            --output "$output_file"
        ;;

    add-all)
        if [ -z "$migration_name" ]; then
            echo "Usage: $0 add-all [context] <migration-name>"
            exit 1
        fi
        echo "[$context_name] Adding migration '$migration_name' for all providers..."
        for p in Sqlite SqlServer Postgres; do
            echo "--- $p ---"
            ctx=$(get_provider_context "$BASE_CONTEXT" "$p")
            dir=$(get_output_dir "$p")
            dotnet ef migrations add "$migration_name" \
                --context "$ctx" \
                --project "$PROJECT" \
                --startup-project "$STARTUP" \
                --output-dir "$dir"
        done
        ;;

    add-all-contexts)
        prov_arg="${2:-Sqlite}"
        migration_name="${3:-}"
        prov=$(get_provider "$prov_arg")

        # If second arg looks like a migration name (not a provider), assume Sqlite
        if [ -z "$prov" ]; then
            prov="Sqlite"
            migration_name="$prov_arg"
        fi

        if [ -z "$migration_name" ]; then
            echo "Usage: $0 add-all-contexts [provider] <migration-name>"
            exit 1
        fi

        echo "Adding migration '$migration_name' for all public contexts ($prov)..."
        for ctx in oluso scim ldap saml; do
            cfg=$(get_context_config "$ctx")
            IFS='|' read -r proj start base_ctx <<< "$cfg"
            provider_ctx=$(get_provider_context "$base_ctx" "$prov")
            dir=$(get_output_dir "$prov")
            echo "--- $ctx ($provider_ctx) ---"
            dotnet ef migrations add "$migration_name" \
                --context "$provider_ctx" \
                --project "$proj" \
                --startup-project "$start" \
                --output-dir "$dir" || { echo "Skipped $ctx (may not exist)"; continue; }
        done
        ;;

    help|*)
        echo "Oluso Migration Helper (Public Contexts)"
        echo ""
        echo "Uses provider-specific DbContext types (Microsoft recommended approach):"
        echo "  - OlusoDbContextSqlite, OlusoDbContextSqlServer, OlusoDbContextPostgres"
        echo "  - Each provider has its own migrations folder and snapshot"
        echo ""
        echo "Usage: $0 <command> [context] [provider] [migration-name]"
        echo ""
        echo "Commands:"
        echo "  add [context] <provider> <name>   Add a new migration"
        echo "  remove [context] <provider>       Remove the last migration"
        echo "  list [context] <provider>         List all migrations"
        echo "  script [context] <provider>       Generate SQL script"
        echo "  add-all [context] <name>          Add migration for all providers"
        echo "  add-all-contexts <name>           Add migration for all contexts (SQLite)"
        echo ""
        echo "Contexts: oluso (default), scim, ldap, saml"
        echo "Providers: Sqlite (default), SqlServer, Postgres"
        echo ""
        echo "Examples:"
        echo "  $0 add Sqlite Initial              # OlusoDbContextSqlite -> Migrations/Sqlite"
        echo "  $0 add SqlServer Initial           # OlusoDbContextSqlServer -> Migrations/SqlServer"
        echo "  $0 add scim Sqlite Initial         # ScimDbContextSqlite -> Migrations/Sqlite"
        echo "  $0 add-all oluso Initial           # All providers for OlusoDbContext"
        echo "  $0 add-all-contexts Sqlite Initial # All contexts with SQLite"
        echo "  $0 list scim Sqlite                # List SCIM Sqlite migrations"
        echo ""
        echo "For private contexts (billing, workflows), use:"
        echo "  ./private/scripts/migrations.sh"
        ;;
esac
