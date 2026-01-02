//! Hello Plugin - A simple Oluso WASM plugin example
//!
//! This plugin demonstrates the interface that WASM plugins must implement
//! to work with the Oluso plugin executor.
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

/// Input from the Oluso plugin executor
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct PluginInput {
    function: String,
    user_id: Option<String>,
    tenant_id: Option<String>,
    input: HashMap<String, serde_json::Value>,
    journey_data: HashMap<String, serde_json::Value>,
}

/// Output to return to the Oluso plugin executor
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PluginOutput {
    success: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    action: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    data: Option<HashMap<String, serde_json::Value>>,
}

impl PluginOutput {
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

/// The main execute function called by Oluso
/// This is the primary entry point for the plugin
#[plugin_fn]
pub fn execute(input_json: String) -> FnResult<String> {
    let input: PluginInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse input: {}", e)))?;

    let output = match input.function.as_str() {
        "execute" | "greet" => greet(&input),
        "validate" => validate(&input),
        "transform" => transform(&input),
        "branch" => branch_example(&input),
        _ => PluginOutput::error(&format!("Unknown function: {}", input.function)),
    };

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}

/// Greet function - returns a greeting message
fn greet(input: &PluginInput) -> PluginOutput {
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
    data.insert("plugin_version".to_string(), serde_json::json!("1.0.0"));

    PluginOutput::success(data)
}

/// Validate function - validates input data
fn validate(input: &PluginInput) -> PluginOutput {
    // Check for required fields
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
        PluginOutput::success(data)
    } else {
        PluginOutput::error(&errors.join("; "))
    }
}

/// Transform function - transforms claims/data
fn transform(input: &PluginInput) -> PluginOutput {
    let mut data = HashMap::new();

    // Copy and transform input data
    for (key, value) in &input.input {
        // Example: uppercase string values
        if let Some(s) = value.as_str() {
            data.insert(
                format!("{}_transformed", key),
                serde_json::json!(s.to_uppercase()),
            );
        } else {
            data.insert(key.clone(), value.clone());
        }
    }

    // Add metadata
    data.insert(
        "transformed_at".to_string(),
        serde_json::json!("2024-01-01T00:00:00Z"),
    );
    data.insert("transformer".to_string(), serde_json::json!("hello-plugin"));

    PluginOutput::success(data)
}

/// Branch example - demonstrates branching based on input
fn branch_example(input: &PluginInput) -> PluginOutput {
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

    PluginOutput::branch(branch_id, data)
}

/// Alternative entry point for validation
#[plugin_fn]
pub fn validate_input(input_json: String) -> FnResult<String> {
    let input: PluginInput = serde_json::from_str(&input_json)
        .map_err(|e| Error::msg(format!("Failed to parse input: {}", e)))?;

    let output = validate(&input);

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
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
                {"value": "marketing", "label": "Marketing"},
                {"value": "support", "label": "Support"}
            ]
        },
        {
            "name": "notes",
            "type": "textarea",
            "label": "Additional Notes",
            "rows": 3
        }
    ]));

    let output = PluginOutput::require_input(form_schema);

    let output_json = serde_json::to_string(&output)
        .map_err(|e| Error::msg(format!("Failed to serialize output: {}", e)))?;

    Ok(output_json)
}
