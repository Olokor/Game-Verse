using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GameVerse.Api.Hosting;

/// <summary>
/// Marks every property whose C# type is not nullable as "required" in the OpenAPI document. Without it the generated
/// TypeScript client (packages/api-schema) types every field as optional, so a missing <c>title</c> would only fail at
/// runtime. Nullable properties (<c>string?</c>, <c>int?</c>) stay optional. (.NET note: an IOpenApiSchemaTransformer
/// edits each generated schema, like a springdoc PropertyCustomizer.)
/// </summary>
internal sealed class OpenApiRequiredProperties : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        if (context.JsonTypeInfo.Kind != JsonTypeInfoKind.Object || schema.Properties is null)
        {
            return Task.CompletedTask;
        }

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (!property.IsGetNullable && schema.Properties.ContainsKey(property.Name))
            {
                schema.Required ??= new HashSet<string>();
                schema.Required.Add(property.Name);
            }
        }

        return Task.CompletedTask;
    }
}
