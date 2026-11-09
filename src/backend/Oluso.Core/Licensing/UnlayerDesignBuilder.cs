using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oluso.Core.Licensing;

/// <summary>
/// Builds valid Unlayer (angular-email-editor) design JSON for seeded email templates.
/// Generated designs are fully editable in the visual email builder.
/// </summary>
public class UnlayerDesignBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly List<Dictionary<string, object>> _rows = [];
    private string _accentColor = "#2563EB";
    private string _backgroundColor = "#F4F4F5";
    private string _contentBackgroundColor = "#FFFFFF";
    private int _rowCounter;
    private int _columnCounter;
    private int _textCounter;
    private int _dividerCounter;
    private int _buttonCounter;
    private int _imageCounter = 0;
    private int _headingCounter;

    private UnlayerDesignBuilder() { }

    public static UnlayerDesignBuilder Create() => new();

    public UnlayerDesignBuilder WithAccentColor(string color)
    {
        _accentColor = color;
        return this;
    }

    public UnlayerDesignBuilder WithBackgroundColor(string color)
    {
        _backgroundColor = color;
        return this;
    }

    /// <summary>
    /// Adds a colored header row with white heading text.
    /// </summary>
    public UnlayerDesignBuilder AddHeader(string title)
    {
        _rowCounter++;
        _columnCounter++;
        _headingCounter++;

        var row = BuildRow($"u_row_{_rowCounter}", $"u_column_{_columnCounter}",
            new Dictionary<string, object>
            {
                ["id"] = $"u_content_heading_{_headingCounter}",
                ["type"] = "heading",
                ["values"] = new Dictionary<string, object>
                {
                    ["containerPadding"] = "30px 40px",
                    ["anchor"] = "",
                    ["headingType"] = "h1",
                    ["fontSize"] = "22px",
                    ["textAlign"] = "center",
                    ["lineHeight"] = "140%",
                    ["linkStyle"] = DefaultLinkStyle("#FFFFFF"),
                    ["fontFamily"] = DefaultFontFamily(),
                    ["fontWeight"] = 700,
                    ["color"] = "#FFFFFF",
                    ["text"] = title,
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["htmlID"] = $"u_content_heading_{_headingCounter}",
                        ["htmlClassNames"] = "u_content_heading"
                    }
                }
            },
            columnValues: new Dictionary<string, object>
            {
                ["backgroundColor"] = "",
                ["padding"] = "0px",
                ["border"] = new Dictionary<string, object>(),
                ["_meta"] = new Dictionary<string, object>
                {
                    ["htmlID"] = $"u_column_{_columnCounter}",
                    ["htmlClassNames"] = "u_column"
                }
            },
            rowBgColor: _accentColor);

        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Adds a text content row. HTML content is placed as-is into the text block.
    /// </summary>
    public UnlayerDesignBuilder AddText(string html, string padding = "20px 40px")
    {
        _rowCounter++;
        _columnCounter++;
        _textCounter++;

        var row = BuildRow($"u_row_{_rowCounter}", $"u_column_{_columnCounter}",
            new Dictionary<string, object>
            {
                ["id"] = $"u_content_text_{_textCounter}",
                ["type"] = "text",
                ["values"] = new Dictionary<string, object>
                {
                    ["containerPadding"] = padding,
                    ["anchor"] = "",
                    ["fontSize"] = "14px",
                    ["textAlign"] = "left",
                    ["lineHeight"] = "170%",
                    ["linkStyle"] = DefaultLinkStyle(),
                    ["fontFamily"] = DefaultFontFamily(),
                    ["text"] = html,
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["htmlID"] = $"u_content_text_{_textCounter}",
                        ["htmlClassNames"] = "u_content_text"
                    }
                }
            },
            columnValues: DefaultColumnValues($"u_column_{_columnCounter}"),
            rowBgColor: _contentBackgroundColor);

        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Adds a horizontal divider row.
    /// </summary>
    public UnlayerDesignBuilder AddDivider(string color = "#E5E7EB", string padding = "10px 40px")
    {
        _rowCounter++;
        _columnCounter++;
        _dividerCounter++;

        var row = BuildRow($"u_row_{_rowCounter}", $"u_column_{_columnCounter}",
            new Dictionary<string, object>
            {
                ["id"] = $"u_content_divider_{_dividerCounter}",
                ["type"] = "divider",
                ["values"] = new Dictionary<string, object>
                {
                    ["containerPadding"] = padding,
                    ["anchor"] = "",
                    ["border"] = new Dictionary<string, object>
                    {
                        ["borderTopWidth"] = "1px",
                        ["borderTopStyle"] = "solid",
                        ["borderTopColor"] = color
                    },
                    ["textAlign"] = "center",
                    ["width"] = "100%",
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["htmlID"] = $"u_content_divider_{_dividerCounter}",
                        ["htmlClassNames"] = "u_content_divider"
                    }
                }
            },
            columnValues: DefaultColumnValues($"u_column_{_columnCounter}"),
            rowBgColor: _contentBackgroundColor);

        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Adds a centered button row.
    /// </summary>
    public UnlayerDesignBuilder AddButton(string text, string url = "{{action_url}}", string? color = null)
    {
        _rowCounter++;
        _columnCounter++;
        _buttonCounter++;

        var row = BuildRow($"u_row_{_rowCounter}", $"u_column_{_columnCounter}",
            new Dictionary<string, object>
            {
                ["id"] = $"u_content_button_{_buttonCounter}",
                ["type"] = "button",
                ["values"] = new Dictionary<string, object>
                {
                    ["containerPadding"] = "20px 40px",
                    ["anchor"] = "",
                    ["href"] = new Dictionary<string, object>
                    {
                        ["name"] = "web",
                        ["values"] = new Dictionary<string, object> { ["href"] = url, ["target"] = "_blank" }
                    },
                    ["buttonColors"] = new Dictionary<string, object>
                    {
                        ["color"] = "#FFFFFF",
                        ["backgroundColor"] = color ?? _accentColor,
                        ["hoverColor"] = "#FFFFFF",
                        ["hoverBackgroundColor"] = color ?? _accentColor
                    },
                    ["size"] = new Dictionary<string, object> { ["autoWidth"] = true, ["width"] = "100%" },
                    ["textAlign"] = "center",
                    ["lineHeight"] = "120%",
                    ["padding"] = "12px 28px",
                    ["border"] = new Dictionary<string, object>(),
                    ["borderRadius"] = "6px",
                    ["fontFamily"] = DefaultFontFamily(),
                    ["fontSize"] = "14px",
                    ["fontWeight"] = 700,
                    ["text"] = $"<span style=\"line-height: 16.8px;\">{text}</span>",
                    ["calculatedWidth"] = 156,
                    ["calculatedHeight"] = 41,
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["htmlID"] = $"u_content_button_{_buttonCounter}",
                        ["htmlClassNames"] = "u_content_button"
                    }
                }
            },
            columnValues: DefaultColumnValues($"u_column_{_columnCounter}"),
            rowBgColor: _contentBackgroundColor);

        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Adds a footer row with small, muted text on a light gray background.
    /// </summary>
    public UnlayerDesignBuilder AddFooter(string html)
    {
        _rowCounter++;
        _columnCounter++;
        _textCounter++;

        var row = BuildRow($"u_row_{_rowCounter}", $"u_column_{_columnCounter}",
            new Dictionary<string, object>
            {
                ["id"] = $"u_content_text_{_textCounter}",
                ["type"] = "text",
                ["values"] = new Dictionary<string, object>
                {
                    ["containerPadding"] = "20px 40px",
                    ["anchor"] = "",
                    ["fontSize"] = "12px",
                    ["color"] = "#71717A",
                    ["textAlign"] = "center",
                    ["lineHeight"] = "160%",
                    ["linkStyle"] = DefaultLinkStyle("#71717A"),
                    ["fontFamily"] = DefaultFontFamily(),
                    ["text"] = html,
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["htmlID"] = $"u_content_text_{_textCounter}",
                        ["htmlClassNames"] = "u_content_text"
                    }
                }
            },
            columnValues: DefaultColumnValues($"u_column_{_columnCounter}"),
            rowBgColor: "#F4F4F5");

        _rows.Add(row);
        return this;
    }

    /// <summary>
    /// Builds the final Unlayer design JSON string.
    /// </summary>
    public string Build()
    {
        var design = new Dictionary<string, object>
        {
            ["counters"] = new Dictionary<string, object>
            {
                ["u_row"] = _rowCounter,
                ["u_column"] = _columnCounter,
                ["u_content_text"] = _textCounter,
                ["u_content_divider"] = _dividerCounter,
                ["u_content_button"] = _buttonCounter,
                ["u_content_image"] = _imageCounter,
                ["u_content_heading"] = _headingCounter
            },
            ["body"] = new Dictionary<string, object>
            {
                ["id"] = "u_body",
                ["rows"] = _rows,
                ["headers"] = Array.Empty<object>(),
                ["footers"] = Array.Empty<object>(),
                ["values"] = new Dictionary<string, object>
                {
                    ["popupPosition"] = "center",
                    ["popupWidth"] = "600px",
                    ["popupHeight"] = "auto",
                    ["borderRadius"] = "10px",
                    ["contentAlign"] = "center",
                    ["contentVerticalAlign"] = "center",
                    ["contentWidth"] = "600px",
                    ["fontFamily"] = DefaultFontFamily(),
                    ["textColor"] = "#18181B",
                    ["backgroundColor"] = _backgroundColor,
                    ["preheaderText"] = "",
                    ["linkStyle"] = new Dictionary<string, object>
                    {
                        ["body"] = true,
                        ["linkColor"] = _accentColor,
                        ["linkHoverColor"] = _accentColor,
                        ["linkUnderline"] = true,
                        ["linkHoverUnderline"] = true
                    },
                    ["_meta"] = new Dictionary<string, object>
                    {
                        ["htmlID"] = "u_body",
                        ["htmlClassNames"] = "u_body"
                    }
                }
            },
            ["schemaVersion"] = 16
        };

        return JsonSerializer.Serialize(design, JsonOptions);
    }

    private Dictionary<string, object> BuildRow(
        string rowId,
        string columnId,
        Dictionary<string, object> content,
        Dictionary<string, object> columnValues,
        string rowBgColor)
    {
        return new Dictionary<string, object>
        {
            ["id"] = rowId,
            ["cells"] = new[] { 1 },
            ["columns"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["id"] = columnId,
                    ["contents"] = new object[] { content },
                    ["values"] = columnValues
                }
            },
            ["values"] = new Dictionary<string, object>
            {
                ["displayCondition"] = (object)null!,
                ["columns"] = false,
                ["backgroundColor"] = "",
                ["columnsBackgroundColor"] = rowBgColor,
                ["backgroundImage"] = new Dictionary<string, object>
                {
                    ["url"] = "",
                    ["fullWidth"] = true,
                    ["repeat"] = "no-repeat",
                    ["size"] = "custom",
                    ["position"] = "center"
                },
                ["padding"] = "0px",
                ["anchor"] = "",
                ["hideDesktop"] = false,
                ["_meta"] = new Dictionary<string, object>
                {
                    ["htmlID"] = rowId,
                    ["htmlClassNames"] = "u_row"
                },
                ["selectable"] = true,
                ["draggable"] = true,
                ["duplicatable"] = true,
                ["deletable"] = true,
                ["hideable"] = true
            }
        };
    }

    private static Dictionary<string, object> DefaultColumnValues(string htmlId) => new()
    {
        ["backgroundColor"] = "",
        ["padding"] = "0px",
        ["border"] = new Dictionary<string, object>(),
        ["_meta"] = new Dictionary<string, object>
        {
            ["htmlID"] = htmlId,
            ["htmlClassNames"] = "u_column"
        }
    };

    private static Dictionary<string, object> DefaultLinkStyle(string? color = null) => new()
    {
        ["inherit"] = true,
        ["linkColor"] = color ?? "#0068A5",
        ["linkHoverColor"] = color ?? "#0068A5",
        ["linkUnderline"] = true,
        ["linkHoverUnderline"] = true
    };

    private static Dictionary<string, object> DefaultFontFamily() => new()
    {
        ["label"] = "Arial",
        ["value"] = "arial,helvetica,sans-serif"
    };
}
