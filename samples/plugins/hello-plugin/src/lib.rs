//! Hello Plugin - A sample Oluso WASM plugin supporting Journey and CRM hooks
//!
//! This plugin demonstrates the interface that WASM plugins must implement
//! to work with the Oluso plugin executor for different domains.
//!
//! To build:
//! ```bash
//! cargo build --target wasm32-unknown-unknown --release
//! ```
//!
//! The output will be in `target/wasm32-unknown-unknown/release/hello_plugin.wasm`

use extism_pdk::*;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

// ============================================================================
// Common Types
// ============================================================================

/// User context passed to hooks
#[derive(Deserialize, Debug)]
#[serde(rename_all = "camelCase")]
struct UserContext {
    id: String,
    email: Option<String>,
    name: Option<String>,
    roles: Vec<String>,
    permissions: Vec<String>,
}

// ============================================================================
// Journey Domain Types
// ============================================================================

/// Input from the Oluso plugin executor (Journey domain)
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct JourneyPluginInput {
    function: String,
    user_id: Option<String>,
    tenant_id: Option<String>,
    input: HashMap<String, serde_json::Value>,
    journey_data: HashMap<String, serde_json::Value>,
}

/// Output for Journey domain
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct JourneyPluginOutput {
    success: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    action: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    data: Option<HashMap<String, serde_json::Value>>,
}

impl JourneyPluginOutput {
    fn success(data: HashMap<String, serde_json::Value>) -> Self {
        Self {
            success: true,
            error: None,
            action: Some("continue".to_string()),
            data: Some(data),
        }
    }

    fn error(message: &str) -> Self {
        Self {
            success: false,
            error: Some(message.to_string()),
            action: Some("fail".to_string()),
            data: None,
        }
    }

    fn require_input(data: HashMap<String, serde_json::Value>) -> Self {
        Self {
            success: true,
            error: None,
            action: Some("require_input".to_string()),
            data: Some(data),
        }
    }

    fn branch(branch_id: &str, data: HashMap<String, serde_json::Value>) -> Self {
        let mut output_data = data;
        output_data.insert("branchId".to_string(), serde_json::json!(branch_id));
        Self {
            success: true,
            error: None,
            action: Some("branch".to_string()),
            data: Some(output_data),
        }
    }
}

// ============================================================================
// CRM Domain Types
// ============================================================================

/// Input from the Oluso plugin executor (CRM domain hooks)
#[derive(Deserialize, Debug)]
#[serde(rename_all = "camelCase")]
struct CRMHookInput {
    tenant_id: String,
    user: Option<UserContext>,
    timestamp: String,
    execution_id: String,
    entity_type: String,
    event: String, // before_insert, after_insert, before_update, after_update, before_delete, after_delete
    record: HashMap<String, serde_json::Value>,
    old_record: Option<HashMap<String, serde_json::Value>>,
    related: Option<HashMap<String, HashMap<String, serde_json::Value>>>,
}

/// Field-level error
#[derive(Serialize)]
struct FieldError {
    field: String,
    message: String,
}

/// Side effect to execute after the hook
#[derive(Serialize)]
struct SideEffect {
    #[serde(rename = "type")]
    effect_type: String,
    parameters: HashMap<String, serde_json::Value>,
}

/// Output for CRM domain hooks
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct CRMHookOutput {
    action: String, // continue, reject, skip
    #[serde(skip_serializing_if = "Option::is_none")]
    message: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    errors: Option<Vec<FieldError>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    record: Option<HashMap<String, serde_json::Value>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    side_effects: Option<Vec<SideEffect>>,
}

impl CRMHookOutput {
    fn continue_with(record: Option<HashMap<String, serde_json::Value>>) -> Self {
        Self {
            action: "continue".to_string(),
            message: None,
            errors: None,
            record,
            side_effects: None,
        }
    }

    fn reject(message: &str) -> Self {
        Self {
            action: "reject".to_string(),
            message: Some(message.to_string()),
            errors: None,
            record: None,
            side_effects: None,
        }
    }

    fn reject_with_errors(errors: Vec<FieldError>) -> Self {
        Self {
            action: "reject".to_string(),
            message: None,
            errors: Some(errors),
            record: None,
            side_effects: None,
        }
    }

    fn skip() -> Self {
        Self {
            action: "skip".to_string(),
            message: None,
            errors: None,
            record: None,
            side_effects: None,
        }
    }

    fn with_side_effects(mut self, effects: Vec<SideEffect>) -> Self {
        self.side_effects = Some(effects);
        self
    }
}

// ============================================================================
// Journey Domain Functions (existing)
// ============================================================================

/// The main execute function for Journey domain
#[plugin_fn]
pub fn execute(input_json: String) -> FnResult<String> {
    let input: JourneyPluginInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse input: {}", e)))?;

    let output = match input.function.as_str() {
        "execute" | "greet" => journey_greet(&input),
        "validate" => journey_validate(&input),
        "transform" => journey_transform(&input),
        "branch" => journey_branch(&input),
        _ => JourneyPluginOutput::error(&format!("Unknown function: {}", input.function)),
    };

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}

fn journey_greet(input: &JourneyPluginInput) -> JourneyPluginOutput {
    let name = input
        .input
        .get("name")
        .and_then(|v| v.as_str())
        .unwrap_or("World");

    let user_id = input.user_id.as_deref().unwrap_or("anonymous");

    let mut data = HashMap::new();
    data.insert(
        "greeting".to_string(),
        serde_json::json!(format!("Hello, {}!", name)),
    );
    data.insert("user_id".to_string(), serde_json::json!(user_id));
    data.insert("plugin_version".to_string(), serde_json::json!("2.0.0"));

    JourneyPluginOutput::success(data)
}

fn journey_validate(input: &JourneyPluginInput) -> JourneyPluginOutput {
    let email = input.input.get("email");
    let age = input.input.get("age");

    let mut errors = Vec::new();

    if email.is_none() || email.and_then(|v| v.as_str()).unwrap_or("").is_empty() {
        errors.push("Email is required");
    } else {
        let email_str = email.and_then(|v| v.as_str()).unwrap_or("");
        if !email_str.contains('@') {
            errors.push("Email must contain @");
        }
    }

    if let Some(age_val) = age {
        if let Some(age_num) = age_val.as_i64() {
            if age_num < 0 || age_num > 150 {
                errors.push("Age must be between 0 and 150");
            }
        } else {
            errors.push("Age must be a number");
        }
    }

    if errors.is_empty() {
        let mut data = HashMap::new();
        data.insert("validated".to_string(), serde_json::json!(true));
        JourneyPluginOutput::success(data)
    } else {
        JourneyPluginOutput::error(&errors.join("; "))
    }
}

fn journey_transform(input: &JourneyPluginInput) -> JourneyPluginOutput {
    let mut data = HashMap::new();

    for (key, value) in &input.input {
        if let Some(s) = value.as_str() {
            data.insert(
                format!("{}_transformed", key),
                serde_json::json!(s.to_uppercase()),
            );
        } else {
            data.insert(key.clone(), value.clone());
        }
    }

    data.insert("transformer".to_string(), serde_json::json!("hello-plugin"));

    JourneyPluginOutput::success(data)
}

fn journey_branch(input: &JourneyPluginInput) -> JourneyPluginOutput {
    let role = input
        .input
        .get("role")
        .and_then(|v| v.as_str())
        .unwrap_or("user");

    let branch_id = match role {
        "admin" => "admin_flow",
        "moderator" => "moderator_flow",
        _ => "default_flow",
    };

    let mut data = HashMap::new();
    data.insert("selected_branch".to_string(), serde_json::json!(branch_id));
    data.insert("role".to_string(), serde_json::json!(role));

    JourneyPluginOutput::branch(branch_id, data)
}

/// Collect additional data from user
#[plugin_fn]
pub fn collect_data(_input_json: String) -> FnResult<String> {
    let mut form_schema = HashMap::new();
    form_schema.insert("title".to_string(), serde_json::json!("Additional Information"));
    form_schema.insert("description".to_string(), serde_json::json!("Please provide the following information"));
    form_schema.insert("fields".to_string(), serde_json::json!([
        {
            "name": "company",
            "type": "text",
            "label": "Company Name",
            "required": true
        },
        {
            "name": "department",
            "type": "select",
            "label": "Department",
            "options": [
                {"value": "engineering", "label": "Engineering"},
                {"value": "sales", "label": "Sales"},
                {"value": "marketing", "label": "Marketing"}
            ]
        }
    ]));

    let output = JourneyPluginOutput::require_input(form_schema);

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}

// ============================================================================
// CRM Domain Functions (new)
// ============================================================================

/// CRM hook handler - main entry point for CRM domain hooks
/// Configure hook binding with domain=CRM, functionName=handle_crm
#[plugin_fn]
pub fn handle_crm(input_json: String) -> FnResult<String> {
    let input: CRMHookInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse CRM input: {}", e)))?;

    let output = match input.event.as_str() {
        "before_insert" => crm_before_insert(&input),
        "after_insert" => crm_after_insert(&input),
        "before_update" => crm_before_update(&input),
        "after_update" => crm_after_update(&input),
        "before_delete" => crm_before_delete(&input),
        "after_delete" => crm_after_delete(&input),
        _ => CRMHookOutput::continue_with(None), // Unknown event, just continue
    };

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}

/// Called before a record is inserted
fn crm_before_insert(input: &CRMHookInput) -> CRMHookOutput {
    let mut modified_record = input.record.clone();
    let mut errors = Vec::new();

    // Example: Validate required fields for Accounts
    if input.entity_type == "Account" || input.entity_type == "accounts" {
        // Check name is not empty
        if let Some(name) = input.record.get("name") {
            if let Some(name_str) = name.as_str() {
                if name_str.trim().is_empty() {
                    errors.push(FieldError {
                        field: "name".to_string(),
                        message: "Account name is required".to_string(),
                    });
                }
            }
        } else {
            errors.push(FieldError {
                field: "name".to_string(),
                message: "Account name is required".to_string(),
            });
        }

        // Example: Auto-set a default value
        if !modified_record.contains_key("source") {
            modified_record.insert("source".to_string(), serde_json::json!("Plugin Default"));
        }
    }

    // Example: Validate Deal amount
    if input.entity_type == "Deal" || input.entity_type == "deals" {
        if let Some(amount) = input.record.get("amount") {
            if let Some(amount_num) = amount.as_f64() {
                if amount_num < 0.0 {
                    errors.push(FieldError {
                        field: "amount".to_string(),
                        message: "Deal amount cannot be negative".to_string(),
                    });
                }
            }
        }
    }

    if !errors.is_empty() {
        return CRMHookOutput::reject_with_errors(errors);
    }

    // Add audit field
    modified_record.insert(
        "_plugin_processed".to_string(),
        serde_json::json!(true),
    );

    CRMHookOutput::continue_with(Some(modified_record))
}

/// Called after a record is inserted
fn crm_after_insert(input: &CRMHookInput) -> CRMHookOutput {
    let mut side_effects = Vec::new();

    // Example: Send notification for high-value deals
    if input.entity_type == "Deal" || input.entity_type == "deals" {
        if let Some(amount) = input.record.get("amount") {
            if let Some(amount_num) = amount.as_f64() {
                if amount_num >= 100000.0 {
                    let mut params = HashMap::new();
                    params.insert("to".to_string(), serde_json::json!("sales-managers@company.com"));
                    params.insert("subject".to_string(), serde_json::json!("High-Value Deal Created"));
                    params.insert("body".to_string(), serde_json::json!(format!(
                        "A new deal worth ${:.2} has been created",
                        amount_num
                    )));

                    side_effects.push(SideEffect {
                        effect_type: "send_email".to_string(),
                        parameters: params,
                    });
                }
            }
        }
    }

    // Example: Log creation to audit webhook
    if input.entity_type == "Account" || input.entity_type == "accounts" {
        let mut params = HashMap::new();
        params.insert("url".to_string(), serde_json::json!("https://audit.example.com/webhook"));
        params.insert("method".to_string(), serde_json::json!("POST"));
        params.insert("body".to_string(), serde_json::json!({
            "event": "account_created",
            "record_id": input.record.get("id"),
            "tenant_id": input.tenant_id,
            "timestamp": input.timestamp
        }));

        side_effects.push(SideEffect {
            effect_type: "http_request".to_string(),
            parameters: params,
        });
    }

    if side_effects.is_empty() {
        CRMHookOutput::continue_with(None)
    } else {
        CRMHookOutput::continue_with(None).with_side_effects(side_effects)
    }
}

/// Called before a record is updated
fn crm_before_update(input: &CRMHookInput) -> CRMHookOutput {
    let mut modified_record = input.record.clone();

    // Example: Prevent changing certain fields after creation
    if input.entity_type == "Deal" || input.entity_type == "deals" {
        if let Some(old_record) = &input.old_record {
            // Don't allow changing the deal type after it's set
            if let (Some(old_type), Some(new_type)) = (old_record.get("type"), input.record.get("type")) {
                if old_type != new_type && old_type.as_str() != Some("") {
                    return CRMHookOutput::reject("Deal type cannot be changed after creation");
                }
            }
        }
    }

    // Example: Track who modified the record
    if let Some(user) = &input.user {
        modified_record.insert(
            "_last_modified_by_plugin".to_string(),
            serde_json::json!(user.id),
        );
    }

    CRMHookOutput::continue_with(Some(modified_record))
}

/// Called after a record is updated
fn crm_after_update(input: &CRMHookInput) -> CRMHookOutput {
    let mut side_effects = Vec::new();

    // Example: Notify on deal stage change
    if input.entity_type == "Deal" || input.entity_type == "deals" {
        if let Some(old_record) = &input.old_record {
            let old_stage = old_record.get("stage").and_then(|v| v.as_str());
            let new_stage = input.record.get("stage").and_then(|v| v.as_str());

            if old_stage != new_stage {
                if new_stage == Some("Closed Won") {
                    let mut params = HashMap::new();
                    params.insert("channel".to_string(), serde_json::json!("#sales-wins"));
                    params.insert("message".to_string(), serde_json::json!(format!(
                        "Deal '{}' has been closed as WON!",
                        input.record.get("name").and_then(|v| v.as_str()).unwrap_or("Unknown")
                    )));

                    side_effects.push(SideEffect {
                        effect_type: "slack_message".to_string(),
                        parameters: params,
                    });
                }
            }
        }
    }

    if side_effects.is_empty() {
        CRMHookOutput::continue_with(None)
    } else {
        CRMHookOutput::continue_with(None).with_side_effects(side_effects)
    }
}

/// Called before a record is deleted
fn crm_before_delete(input: &CRMHookInput) -> CRMHookOutput {
    // Example: Prevent deletion of high-value accounts
    if input.entity_type == "Account" || input.entity_type == "accounts" {
        if let Some(revenue) = input.record.get("annualRevenue") {
            if let Some(revenue_num) = revenue.as_f64() {
                if revenue_num >= 1000000.0 {
                    return CRMHookOutput::reject(
                        "Cannot delete accounts with annual revenue over $1M. Please contact admin."
                    );
                }
            }
        }
    }

    // Example: Prevent deletion of deals in certain stages
    if input.entity_type == "Deal" || input.entity_type == "deals" {
        if let Some(stage) = input.record.get("stage").and_then(|v| v.as_str()) {
            if stage == "Closed Won" || stage == "Closed Lost" {
                return CRMHookOutput::reject(
                    "Cannot delete closed deals. They must be archived instead."
                );
            }
        }
    }

    CRMHookOutput::continue_with(None)
}

/// Called after a record is deleted
fn crm_after_delete(input: &CRMHookInput) -> CRMHookOutput {
    let mut side_effects = Vec::new();

    // Example: Log deletion to compliance system
    let mut params = HashMap::new();
    params.insert("url".to_string(), serde_json::json!("https://compliance.example.com/deletions"));
    params.insert("method".to_string(), serde_json::json!("POST"));
    params.insert("body".to_string(), serde_json::json!({
        "entity_type": input.entity_type,
        "record_id": input.record.get("id"),
        "deleted_by": input.user.as_ref().map(|u| &u.id),
        "tenant_id": input.tenant_id,
        "timestamp": input.timestamp
    }));

    side_effects.push(SideEffect {
        effect_type: "http_request".to_string(),
        parameters: params,
    });

    CRMHookOutput::continue_with(None).with_side_effects(side_effects)
}

// ============================================================================
// Validation entry points (can be called directly)
// ============================================================================

/// Validate Account records
#[plugin_fn]
pub fn validate_account(input_json: String) -> FnResult<String> {
    let input: CRMHookInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse input: {}", e)))?;

    let mut errors = Vec::new();

    // Validate name
    if let Some(name) = input.record.get("name") {
        if let Some(name_str) = name.as_str() {
            if name_str.len() < 2 {
                errors.push(FieldError {
                    field: "name".to_string(),
                    message: "Account name must be at least 2 characters".to_string(),
                });
            }
        }
    } else {
        errors.push(FieldError {
            field: "name".to_string(),
            message: "Account name is required".to_string(),
        });
    }

    // Validate website format
    if let Some(website) = input.record.get("website") {
        if let Some(website_str) = website.as_str() {
            if !website_str.is_empty() && !website_str.starts_with("http") {
                errors.push(FieldError {
                    field: "website".to_string(),
                    message: "Website must start with http:// or https://".to_string(),
                });
            }
        }
    }

    let output = if errors.is_empty() {
        CRMHookOutput::continue_with(None)
    } else {
        CRMHookOutput::reject_with_errors(errors)
    };

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}

/// Validate Deal records
#[plugin_fn]
pub fn validate_deal(input_json: String) -> FnResult<String> {
    let input: CRMHookInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse input: {}", e)))?;

    let mut errors = Vec::new();

    // Validate amount
    if let Some(amount) = input.record.get("amount") {
        if let Some(amount_num) = amount.as_f64() {
            if amount_num < 0.0 {
                errors.push(FieldError {
                    field: "amount".to_string(),
                    message: "Amount cannot be negative".to_string(),
                });
            }
        }
    }

    // Validate probability
    if let Some(probability) = input.record.get("probability") {
        if let Some(prob_num) = probability.as_f64() {
            if prob_num < 0.0 || prob_num > 100.0 {
                errors.push(FieldError {
                    field: "probability".to_string(),
                    message: "Probability must be between 0 and 100".to_string(),
                });
            }
        }
    }

    // Validate close date is in future for open deals
    if let Some(stage) = input.record.get("stage").and_then(|v| v.as_str()) {
        if !stage.starts_with("Closed") {
            // Could validate close_date is in future here
        }
    }

    let output = if errors.is_empty() {
        CRMHookOutput::continue_with(None)
    } else {
        CRMHookOutput::reject_with_errors(errors)
    };

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}
