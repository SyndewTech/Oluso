#!/bin/bash
# Oluso EF Core Migration Helper
# Usage: ./scripts/migrations.sh <command> [context] <provider> [migration-name]

set -e

# Get context configuration (public contexts only)
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
    echo "Valid contexts: oluso, scim, ldap"
    echo ""
    echo "For private contexts (billing, workflows), use:"
    echo "  ./private/scripts/migrations.sh"
    exit 1
fi

IFS='|' read -r PROJECT STARTUP CONTEXT <<< "$config"

if [ -z "$provider" ] && [ "$command" != "help" ]; then
    provider="Sqlite"
fi

if [ -n "$provider" ] && [ -z "$(get_provider "$provider")" ]; then
    echo "Unknown provider: $provider"
    echo "Supported: Sqlite, SqlServer, Postgres"
    exit 1
fi

output_dir=$(get_output_dir "$provider")

case "$command" in
    add)
        if [ -z "$migration_name" ]; then
            echo "Usage: $0 add [context] <provider> <migration-name>"
            echo "       $0 add <provider> <migration-name>  (uses oluso context)"
            exit 1
        fi
        echo "[$context_name] Adding migration '$migration_name' for $provider..."
        dotnet ef migrations add "$migration_name" \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP" \
            --output-dir "$output_dir" \
            -- --provider "$provider"
        ;;

    remove)
        echo "[$context_name] Removing last migration for $provider..."
        dotnet ef migrations remove \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP" \
            -- --provider "$provider"
        ;;

    list)
        echo "[$context_name] Listing migrations for $provider..."
        dotnet ef migrations list \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP" \
            -- --provider "$provider"
        ;;

    script)
        output_file="migrations-${context_name}-$(echo "$provider" | tr '[:upper:]' '[:lower:]').sql"
        echo "[$context_name] Generating SQL script for $provider -> $output_file"
        dotnet ef migrations script --idempotent \
            --context "$CONTEXT" \
            --project "$PROJECT" \
            --startup-project "$STARTUP" \
            --output "$output_file" \
            -- --provider "$provider"
        ;;

    add-all)
        if [ -z "$migration_name" ]; then
            echo "Usage: $0 add-all [context] <migration-name>"
            exit 1
        fi
        echo "[$context_name] Adding migration '$migration_name' for all providers..."
        for p in Sqlite SqlServer Postgres; do
            echo "--- $p ---"
            dir=$(get_output_dir "$p")
            dotnet ef migrations add "$migration_name" \
                --context "$CONTEXT" \
                --project "$PROJECT" \
                --startup-project "$STARTUP" \
                --output-dir "$dir" \
                -- --provider "$p"
        done
        ;;

    add-all-contexts)
        migration_name="${2:-}"
        if [ -z "$migration_name" ]; then
            echo "Usage: $0 add-all-contexts <migration-name>"
            exit 1
        fi
        echo "Adding migration '$migration_name' for all public contexts (SQLite only)..."
        for ctx in oluso scim ldap; do
            cfg=$(get_context_config "$ctx")
            IFS='|' read -r proj start ctxname <<< "$cfg"
            echo "--- $ctx ($ctxname) ---"
            dotnet ef migrations add "$migration_name" \
                --context "$ctxname" \
                --project "$proj" \
                --startup-project "$start" \
                --output-dir "Migrations/Sqlite" \
                -- --provider Sqlite || echo "Skipped $ctx (may not exist)"
        done
        ;;

    help|*)
        echo "Oluso Migration Helper (Public Contexts)"
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
        echo "Contexts: oluso (default), scim, ldap"
        echo "Providers: Sqlite (default), SqlServer, Postgres"
        echo ""
        echo "Examples:"
        echo "  $0 add Sqlite AddUserFields              # OlusoDbContext"
        echo "  $0 add scim Sqlite Initial               # ScimDbContext"
        echo "  $0 add ldap Sqlite Initial               # LdapDbContext"
        echo "  $0 add-all oluso AddNewFeature           # All providers for OlusoDbContext"
        echo "  $0 list scim Sqlite                      # List SCIM migrations"
        echo ""
        echo "For private contexts (billing, workflows), use:"
        echo "  ./private/scripts/migrations.sh"
        ;;
esac
