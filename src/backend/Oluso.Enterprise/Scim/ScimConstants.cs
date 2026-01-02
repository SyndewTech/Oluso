namespace Oluso.Enterprise.Scim;

/// <summary>
/// SCIM 2.0 Constants (RFC 7643/7644)
/// </summary>
public static class ScimConstants
{
    /// <summary>
    /// SCIM Schema URNs
    /// </summary>
    public static class Schemas
    {
        public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";
        public const string Group = "urn:ietf:params:scim:schemas:core:2.0:Group";
        public const string EnterpriseUser = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";
        public const string Schema = "urn:ietf:params:scim:schemas:core:2.0:Schema";
        public const string ResourceType = "urn:ietf:params:scim:schemas:core:2.0:ResourceType";
        public const string ServiceProviderConfig = "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig";
        public const string ListResponse = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
        public const string Error = "urn:ietf:params:scim:api:messages:2.0:Error";
        public const string PatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
        public const string BulkRequest = "urn:ietf:params:scim:api:messages:2.0:BulkRequest";
        public const string BulkResponse = "urn:ietf:params:scim:api:messages:2.0:BulkResponse";
        public const string SearchRequest = "urn:ietf:params:scim:api:messages:2.0:SearchRequest";
    }

    /// <summary>
    /// SCIM Content Types
    /// </summary>
    public static class ContentTypes
    {
        public const string Scim = "application/scim+json";
        public const string Json = "application/json";
    }

    /// <summary>
    /// SCIM Attribute mutability values
    /// </summary>
    public static class Mutability
    {
        public const string ReadOnly = "readOnly";
        public const string ReadWrite = "readWrite";
        public const string Immutable = "immutable";
        public const string WriteOnly = "writeOnly";
    }

    /// <summary>
    /// SCIM Attribute returned values
    /// </summary>
    public static class Returned
    {
        public const string Always = "always";
        public const string Never = "never";
        public const string Default = "default";
        public const string Request = "request";
    }

    /// <summary>
    /// SCIM Attribute uniqueness values
    /// </summary>
    public static class Uniqueness
    {
        public const string None = "none";
        public const string Server = "server";
        public const string Global = "global";
    }

    /// <summary>
    /// SCIM Attribute types
    /// </summary>
    public static class Types
    {
        public const string String = "string";
        public const string Boolean = "boolean";
        public const string Decimal = "decimal";
        public const string Integer = "integer";
        public const string DateTime = "dateTime";
        public const string Reference = "reference";
        public const string Complex = "complex";
        public const string Binary = "binary";
    }

    /// <summary>
    /// SCIM Patch operation types
    /// </summary>
    public static class PatchOp
    {
        public const string Add = "add";
        public const string Remove = "remove";
        public const string Replace = "replace";
    }

    /// <summary>
    /// Common SCIM filter operators
    /// </summary>
    public static class FilterOperators
    {
        public const string Eq = "eq";
        public const string Ne = "ne";
        public const string Co = "co";
        public const string Sw = "sw";
        public const string Ew = "ew";
        public const string Gt = "gt";
        public const string Ge = "ge";
        public const string Lt = "lt";
        public const string Le = "le";
        public const string Pr = "pr";
        public const string And = "and";
        public const string Or = "or";
        public const string Not = "not";
    }

    /// <summary>
    /// Default API path prefix
    /// </summary>
    public const string DefaultBasePath = "/scim/v2";
}
