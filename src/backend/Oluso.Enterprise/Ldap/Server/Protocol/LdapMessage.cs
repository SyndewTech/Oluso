using System.Formats.Asn1;

namespace Oluso.Enterprise.Ldap.Server.Protocol;

/// <summary>
/// LDAP message types (RFC 4511)
/// </summary>
public enum LdapOperation
{
    BindRequest = 0,
    BindResponse = 1,
    UnbindRequest = 2,
    SearchRequest = 3,
    SearchResultEntry = 4,
    SearchResultDone = 5,
    ModifyRequest = 6,
    ModifyResponse = 7,
    AddRequest = 8,
    AddResponse = 9,
    DeleteRequest = 10,
    DeleteResponse = 11,
    ModifyDnRequest = 12,
    ModifyDnResponse = 13,
    CompareRequest = 14,
    CompareResponse = 15,
    AbandonRequest = 16,
    SearchResultReference = 19,
    ExtendedRequest = 23,
    ExtendedResponse = 24,
    IntermediateResponse = 25
}

/// <summary>
/// LDAP result codes (RFC 4511)
/// </summary>
public enum LdapResultCode
{
    Success = 0,
    OperationsError = 1,
    ProtocolError = 2,
    TimeLimitExceeded = 3,
    SizeLimitExceeded = 4,
    CompareFalse = 5,
    CompareTrue = 6,
    AuthMethodNotSupported = 7,
    StrongerAuthRequired = 8,
    Referral = 10,
    AdminLimitExceeded = 11,
    UnavailableCriticalExtension = 12,
    ConfidentialityRequired = 13,
    SaslBindInProgress = 14,
    NoSuchAttribute = 16,
    UndefinedAttributeType = 17,
    InappropriateMatching = 18,
    ConstraintViolation = 19,
    AttributeOrValueExists = 20,
    InvalidAttributeSyntax = 21,
    NoSuchObject = 32,
    AliasProblem = 33,
    InvalidDnSyntax = 34,
    AliasDereferencingProblem = 36,
    InappropriateAuthentication = 48,
    InvalidCredentials = 49,
    InsufficientAccessRights = 50,
    Busy = 51,
    Unavailable = 52,
    UnwillingToPerform = 53,
    LoopDetect = 54,
    NamingViolation = 64,
    ObjectClassViolation = 65,
    NotAllowedOnNonLeaf = 66,
    NotAllowedOnRdn = 67,
    EntryAlreadyExists = 68,
    ObjectClassModsProhibited = 69,
    AffectsMultipleDsas = 71,
    Other = 80
}

/// <summary>
/// Search scope
/// </summary>
public enum SearchScope
{
    BaseObject = 0,
    SingleLevel = 1,
    WholeSubtree = 2
}

/// <summary>
/// Deref aliases
/// </summary>
public enum DerefAliases
{
    NeverDerefAliases = 0,
    DerefInSearching = 1,
    DerefFindingBaseObj = 2,
    DerefAlways = 3
}

/// <summary>
/// Base LDAP message
/// </summary>
public abstract class LdapMessage
{
    public int MessageId { get; set; }
    public abstract LdapOperation Operation { get; }

    public abstract byte[] Encode();

    public static LdapMessage? Decode(byte[] data)
    {
        try
        {
            var reader = new AsnReader(data, AsnEncodingRules.BER);
            var sequence = reader.ReadSequence();

            var messageId = (int)sequence.ReadInteger();

            var tag = sequence.PeekTag();
            var operation = (LdapOperation)tag.TagValue;

            return operation switch
            {
                LdapOperation.BindRequest => BindRequest.DecodeRequest(messageId, sequence),
                LdapOperation.UnbindRequest => new UnbindRequest { MessageId = messageId },
                LdapOperation.SearchRequest => SearchRequest.DecodeRequest(messageId, sequence),
                LdapOperation.ExtendedRequest => ExtendedRequest.DecodeRequest(messageId, sequence),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// LDAP Bind Request
/// </summary>
public class BindRequest : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.BindRequest;
    public int Version { get; set; } = 3;
    public string Name { get; set; } = string.Empty;
    public byte[] Authentication { get; set; } = Array.Empty<byte>();
    public bool IsSimpleAuth => true;

    public string? GetSimplePassword()
    {
        return System.Text.Encoding.UTF8.GetString(Authentication);
    }

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.BindRequest)))
            {
                writer.WriteInteger(Version);
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(Name));
                writer.WriteOctetString(Authentication, new Asn1Tag(TagClass.ContextSpecific, 0));
            }
        }
        return writer.Encode();
    }

    internal static BindRequest DecodeRequest(int messageId, AsnReader sequence)
    {
        var bindRequest = sequence.ReadSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.BindRequest));

        var version = (int)bindRequest.ReadInteger();
        var name = System.Text.Encoding.UTF8.GetString(bindRequest.ReadOctetString());
        var auth = bindRequest.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));

        return new BindRequest
        {
            MessageId = messageId,
            Version = version,
            Name = name,
            Authentication = auth
        };
    }
}

/// <summary>
/// LDAP Bind Response
/// </summary>
public class BindResponse : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.BindResponse;
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDn { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.BindResponse)))
            {
                writer.WriteEnumeratedValue(ResultCode);
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(MatchedDn));
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(DiagnosticMessage));
            }
        }
        return writer.Encode();
    }
}

/// <summary>
/// LDAP Unbind Request
/// </summary>
public class UnbindRequest : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.UnbindRequest;

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            writer.WriteNull(new Asn1Tag(TagClass.Application, (int)LdapOperation.UnbindRequest));
        }
        return writer.Encode();
    }
}

/// <summary>
/// LDAP Search Request
/// </summary>
public class SearchRequest : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.SearchRequest;
    public string BaseDn { get; set; } = string.Empty;
    public SearchScope Scope { get; set; }
    public DerefAliases DerefAliases { get; set; }
    public int SizeLimit { get; set; }
    public int TimeLimit { get; set; }
    public bool TypesOnly { get; set; }
    public LdapFilter? Filter { get; set; }
    public List<string> Attributes { get; set; } = new();

    public override byte[] Encode()
    {
        throw new NotImplementedException("Search requests are not sent by server");
    }

    internal static SearchRequest DecodeRequest(int messageId, AsnReader sequence)
    {
        var searchRequest = sequence.ReadSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.SearchRequest));

        var baseDn = System.Text.Encoding.UTF8.GetString(searchRequest.ReadOctetString());
        var scope = searchRequest.ReadEnumeratedValue<SearchScope>();
        var derefAliases = searchRequest.ReadEnumeratedValue<DerefAliases>();
        var sizeLimit = (int)searchRequest.ReadInteger();
        var timeLimit = (int)searchRequest.ReadInteger();
        var typesOnly = searchRequest.ReadBoolean();

        var filter = LdapFilter.Decode(searchRequest);

        var attributes = new List<string>();
        var attrSequence = searchRequest.ReadSequence();
        while (attrSequence.HasData)
        {
            attributes.Add(System.Text.Encoding.UTF8.GetString(attrSequence.ReadOctetString()));
        }

        return new SearchRequest
        {
            MessageId = messageId,
            BaseDn = baseDn,
            Scope = scope,
            DerefAliases = derefAliases,
            SizeLimit = sizeLimit,
            TimeLimit = timeLimit,
            TypesOnly = typesOnly,
            Filter = filter,
            Attributes = attributes
        };
    }
}

/// <summary>
/// LDAP Search Result Entry
/// </summary>
public class SearchResultEntry : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.SearchResultEntry;
    public string ObjectName { get; set; } = string.Empty;
    public Dictionary<string, List<string>> Attributes { get; set; } = new();

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.SearchResultEntry)))
            {
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(ObjectName));
                using (writer.PushSequence())
                {
                    foreach (var attr in Attributes)
                    {
                        using (writer.PushSequence())
                        {
                            writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(attr.Key));
                            using (writer.PushSetOf())
                            {
                                foreach (var value in attr.Value)
                                {
                                    writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(value));
                                }
                            }
                        }
                    }
                }
            }
        }
        return writer.Encode();
    }
}

/// <summary>
/// LDAP Search Result Done
/// </summary>
public class SearchResultDone : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.SearchResultDone;
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDn { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.SearchResultDone)))
            {
                writer.WriteEnumeratedValue(ResultCode);
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(MatchedDn));
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(DiagnosticMessage));
            }
        }
        return writer.Encode();
    }
}

/// <summary>
/// LDAP Extended Request (RFC 4511)
/// Used for operations like STARTTLS (OID: 1.3.6.1.4.1.1466.20037)
/// </summary>
public class ExtendedRequest : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.ExtendedRequest;

    /// <summary>
    /// The OID of the extended operation
    /// </summary>
    public string RequestName { get; set; } = string.Empty;

    /// <summary>
    /// Optional request value (operation-specific data)
    /// </summary>
    public byte[]? RequestValue { get; set; }

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.ExtendedRequest)))
            {
                // requestName [0] LDAPOID
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(RequestName),
                    new Asn1Tag(TagClass.ContextSpecific, 0));

                // requestValue [1] OCTET STRING OPTIONAL
                if (RequestValue != null)
                {
                    writer.WriteOctetString(RequestValue, new Asn1Tag(TagClass.ContextSpecific, 1));
                }
            }
        }
        return writer.Encode();
    }

    internal static ExtendedRequest DecodeRequest(int messageId, AsnReader sequence)
    {
        var extendedRequest = sequence.ReadSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.ExtendedRequest));

        // requestName [0] LDAPOID
        var requestName = System.Text.Encoding.UTF8.GetString(
            extendedRequest.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0)));

        // requestValue [1] OCTET STRING OPTIONAL
        byte[]? requestValue = null;
        if (extendedRequest.HasData)
        {
            var nextTag = extendedRequest.PeekTag();
            if (nextTag.TagClass == TagClass.ContextSpecific && nextTag.TagValue == 1)
            {
                requestValue = extendedRequest.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 1));
            }
        }

        return new ExtendedRequest
        {
            MessageId = messageId,
            RequestName = requestName,
            RequestValue = requestValue
        };
    }
}

/// <summary>
/// LDAP Extended Response (RFC 4511)
/// </summary>
public class ExtendedResponse : LdapMessage
{
    public override LdapOperation Operation => LdapOperation.ExtendedResponse;
    public LdapResultCode ResultCode { get; set; }
    public string MatchedDn { get; set; } = string.Empty;
    public string DiagnosticMessage { get; set; } = string.Empty;

    /// <summary>
    /// The OID of the extended operation (echoes request)
    /// </summary>
    public string? ResponseName { get; set; }

    /// <summary>
    /// Optional response value (operation-specific data)
    /// </summary>
    public byte[]? ResponseValue { get; set; }

    public override byte[] Encode()
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(MessageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, (int)LdapOperation.ExtendedResponse)))
            {
                // LDAPResult components
                writer.WriteEnumeratedValue(ResultCode);
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(MatchedDn));
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(DiagnosticMessage));

                // responseName [10] LDAPOID OPTIONAL
                if (!string.IsNullOrEmpty(ResponseName))
                {
                    writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(ResponseName),
                        new Asn1Tag(TagClass.ContextSpecific, 10));
                }

                // responseValue [11] OCTET STRING OPTIONAL
                if (ResponseValue != null)
                {
                    writer.WriteOctetString(ResponseValue, new Asn1Tag(TagClass.ContextSpecific, 11));
                }
            }
        }
        return writer.Encode();
    }
}
