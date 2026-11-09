using Oluso.Core.UserJourneys;

namespace Oluso.Enterprise.Verification.Journey;

/// <summary>Adds the business verification step, its settings and branches to the journey builder.</summary>
public sealed class BusinessVerificationStepConfiguration : IConfigureStepHandlers
{
    public const string ModuleName = "Oluso.Enterprise.Verification";

    public void Configure(IExtendedStepHandlerRegistry registry)
    {
        registry.Register<BusinessVerificationStepHandler>(BusinessVerificationStepHandler.StepTypeName, step => step
            .WithDisplayName("Business Verification")
            .WithDescription("Checks a business against its company registry (e.g. Nigeria's CAC), or through the provider's hosted flow, and the applicant's right to act for it. Resumes where an earlier attempt left off. Outputs kyb_status, kyb_business_id, kyb_applicant_id, kyb_business_name, kyb_registration_number, kyb_country and kyb_relationship.")
            .InCategory("Verification")
            .WithIcon("building-office-2")
            .FromModule(ModuleName)
            .WithConfigurationSchema(Schema())
            .WithBranch(BusinessVerificationStepHandler.VerifiedBranch, "Verified", "The applicant may act for the business")
            .WithBranch(BusinessVerificationStepHandler.NotVerifiedBranch, "Not verified", "Not found, not trading, a name or director mismatch, or the provider or a reviewer said no. Without this branch the reason is shown with a retry.")
            .WithBranch(BusinessVerificationStepHandler.PendingBranch, "Pending", "Waiting on the provider's decision or a reviewer (e.g. finish sign-up and provision later from the business.verification_completed event). Without this branch the applicant waits on a status page.")
            .WithBranch(BusinessVerificationStepHandler.ProviderUnavailableBranch, "Provider unavailable", "The provider couldn't answer. Without this branch the applicant is asked to try again."));
    }

    private static Dictionary<string, object> Schema() => new()
    {
        ["allowedCountries"] = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["items"] = "string",
            ["required"] = true,
            ["description"] = "Countries the applicant can choose. Each needs a provider routed under Settings → Business verification.",
            ["x-enumSource"] = new Dictionary<string, object>
            {
                ["endpoint"] = "/business-verification/countries",
                ["valueField"] = "code",
                ["labelField"] = "name",
                ["descriptionField"] = "providerName",
            },
        },
        ["mode"] = new
        {
            type = "string",
            @enum = new[] { "lookup", "hosted" },
            @default = "lookup",
            description = "lookup: an instant registry check. hosted: the provider's own KYB flow (Dojah EasyOnboard) opens in the page and the outcome arrives by webhook; the applicant waits on a status page meanwhile.",
        },
        ["requireNameMatch"] = new { type = "boolean", @default = true, description = "Ask for the business name and require it to match the registered name. Case, punctuation, '&'/'and' and the words Ltd, Limited, PLC, LLC, LLP, LP, Inc, Incorporated, Nig, Nigeria and The are ignored." },
        ["requireActive"] = new { type = "boolean", @default = true, description = "Treat a business the registry lists as inactive, struck off, dissolved or in liquidation as not verified" },
        ["applicantEvidence"] = new
        {
            type = "string",
            @enum = new[] { "none", "director_match", "documents", "director_or_documents" },
            @default = "none",
            description = "What the applicant must show to act for the business. director_match: their name is among the registry's directors. documents: they upload documents (e.g. a letter of authority) a reviewer approves. director_or_documents: directors go straight through, anyone else uploads documents.",
        },
        ["directorRoles"] = new { type = "array", items = "string", @default = BusinessVerificationStepHandler.DefaultDirectorRoles, description = "Registry roles that count as a director (matched within the role text, any case)" },
        ["firstNameClaim"] = new { type = "string", @default = "given_name", description = "Journey value holding the applicant's first name for the director match; the signed-in user's profile is used when it's empty" },
        ["lastNameClaim"] = new { type = "string", @default = "family_name", description = "Journey value holding the applicant's last name for the director match; the signed-in user's profile is used when it's empty" },
        ["documentTypes"] = new { type = "array", items = "string", @enum = new[] { "pdf", "jpeg", "png", "webp", "heic" }, @default = BusinessVerificationStepHandler.DefaultDocumentTypes, description = "Kinds of supporting document accepted, recognised by content" },
        ["maxDocumentSizeMb"] = new { type = "number", @default = BusinessVerificationStepHandler.DefaultMaxDocumentSizeMb, description = "Largest supporting document, in MB" },
        ["maxDocuments"] = new { type = "number", @default = BusinessVerificationStepHandler.DefaultMaxDocuments, description = "Most supporting documents per submission" },
        ["allowRetry"] = new { type = "boolean", @default = true, description = "After a rejection, let the applicant correct their details and try again" },
        ["pendingWaitSeconds"] = new { type = "number", @default = BusinessVerificationStepHandler.DefaultPendingWaitSeconds, description = "How long the waiting page checks for a decision before offering to email the outcome (at least 30)" },
        ["countryClaim"] = new { type = "string", description = "Journey value an earlier step wrote with the country code. Optional with a single allowed country." },
        ["registrationNumberClaim"] = new { type = "string", description = "Journey value an earlier step wrote with the registration number. When it's set and filled, the step verifies (or resumes) without showing its form." },
        ["registrationTypeClaim"] = new { type = "string", description = "Journey value with the registration type (Nigeria: RC, BN, IT, LP, LLP) when the number has no prefix" },
        ["businessNameClaim"] = new { type = "string", description = "Journey value an earlier step wrote with the business name" },
    };
}
