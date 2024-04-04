// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace Microsoft.AspNetCore.OpenApi;

internal sealed class OpenApiDocumentService(
    [ServiceKey] string documentName,
    IApiDescriptionGroupCollectionProvider apiDescriptionGroupCollectionProvider,
    IHostEnvironment hostEnvironment,
    IOptionsMonitor<OpenApiOptions> optionsMonitor)
{
    private readonly OpenApiOptions _options = optionsMonitor.Get(documentName);

    public Task<OpenApiDocument> GetOpenApiDocumentAsync()
    {
        // For good hygiene, operation-level tags must also appear in the document-level
        // tags collection. This set captures all tags that have been seen so far.
        HashSet<OpenApiTag> capturedTags = new(OpenApiTagComparer.Instance);
        var document = new OpenApiDocument
        {
            Info = GetOpenApiInfo(),
            Paths = GetOpenApiPaths(capturedTags),
            Tags = [.. capturedTags]
        };
        return Task.FromResult(document);
    }

    // Note: Internal for testing.
    internal OpenApiInfo GetOpenApiInfo()
    {
        return new OpenApiInfo
        {
            Title = $"{hostEnvironment.ApplicationName} | {documentName}",
            Version = OpenApiConstants.DefaultOpenApiVersion
        };
    }

    /// <summary>
    /// Gets the OpenApiPaths for the document based on the ApiDescriptions.
    /// </summary>
    /// <remarks>
    /// At this point in the construction of the OpenAPI document, we run
    /// each API description through the `ShouldInclude` delegate defined in
    /// the object to support filtering each
    /// description instance into its appropriate document.
    /// </remarks>
    private OpenApiPaths GetOpenApiPaths(HashSet<OpenApiTag> capturedTags)
    {
        var descriptionsByPath = apiDescriptionGroupCollectionProvider.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(_options.ShouldInclude)
            .GroupBy(apiDescription => apiDescription.MapRelativePathToItemPath());
        var paths = new OpenApiPaths();
        foreach (var descriptions in descriptionsByPath)
        {
            Debug.Assert(descriptions.Key != null, "Relative path mapped to OpenApiPath key cannot be null.");
            paths.Add(descriptions.Key, new OpenApiPathItem { Operations = GetOperations(descriptions, capturedTags) });
        }
        return paths;
    }

    private static Dictionary<OperationType, OpenApiOperation> GetOperations(IGrouping<string?, ApiDescription> descriptions, HashSet<OpenApiTag> capturedTags)
    {
        var operations = new Dictionary<OperationType, OpenApiOperation>();
        foreach (var description in descriptions)
        {
            operations[description.GetOperationType()] = GetOperation(description, capturedTags);
        }
        return operations;
    }

    private static OpenApiOperation GetOperation(ApiDescription description, HashSet<OpenApiTag> capturedTags)
    {
        var tags = GetTags(description);
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                capturedTags.Add(tag);
            }
        }
        var operation = new OpenApiOperation
        {
            Summary = GetSummary(description),
            Description = GetDescription(description),
            Responses = GetResponses(description),
            Tags = tags,
        };
        return operation;
    }

    private static string? GetSummary(ApiDescription description)
        => description.ActionDescriptor.EndpointMetadata.OfType<IEndpointSummaryMetadata>().LastOrDefault()?.Summary;

    private static string? GetDescription(ApiDescription description)
        => description.ActionDescriptor.EndpointMetadata.OfType<IEndpointDescriptionMetadata>().LastOrDefault()?.Description;

    private static List<OpenApiTag>? GetTags(ApiDescription description)
    {
        var actionDescriptor = description.ActionDescriptor;
        if (actionDescriptor.EndpointMetadata?.OfType<ITagsMetadata>().LastOrDefault() is { } tagsMetadata)
        {
            return tagsMetadata.Tags.Select(tag => new OpenApiTag { Name = tag }).ToList();
        }
        // If no tags are specified, use the controller name as the tag. This effectively
        // allows us to group endpoints by the "resource" concept (e.g. users, todos, etc.)
        return [new OpenApiTag { Name = description.ActionDescriptor.RouteValues["controller"] }];
    }

    private static OpenApiResponses GetResponses(ApiDescription description)
    {
        var supportedResponseTypes = description.SupportedResponseTypes.DefaultIfEmpty(new ApiResponseType { StatusCode = 200 });

        var responses = new OpenApiResponses();
        foreach (var responseType in supportedResponseTypes)
        {
            var statusCode = responseType.IsDefaultResponse ? StatusCodes.Status200OK : responseType.StatusCode;
            responses.Add(statusCode.ToString(CultureInfo.InvariantCulture), GetResponse(description, statusCode, responseType));
        }
        return responses;
    }

    private static OpenApiResponse GetResponse(ApiDescription apiDescription, int statusCode, ApiResponseType apiResponseType)
    {
        var description = ReasonPhrases.GetReasonPhrase(statusCode);

        HashSet<string> responseContentTypes = [];

        var explicitContentTypes = apiDescription.ActionDescriptor.EndpointMetadata
            .OfType<ProducesAttribute>()
            .SelectMany(attr => attr.ContentTypes);
        foreach (var contentType in explicitContentTypes)
        {
            responseContentTypes.Add(contentType);
        }

        var apiResponseFormatContentTypes = apiResponseType.ApiResponseFormats
            .Select(responseFormat => responseFormat.MediaType);
        foreach (var contentType in apiResponseFormatContentTypes)
        {
            responseContentTypes.Add(contentType);
        }

        var content = new Dictionary<string, OpenApiMediaType>();
        foreach (var contentType in responseContentTypes)
        {
            content[contentType] = new OpenApiMediaType();
        }

        return new OpenApiResponse
        {
            Description = description,
            Content = content
        };
    }
}
