using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Cqc.Core;

namespace Cqc.Server.Reporting;

internal static class ReportSerialization
{
    private static readonly HashSet<Type> AssessmentTypes = [typeof(EntityScore), typeof(EntityPageItem), typeof(AssessmentResult)];
    private static readonly JsonSerializerOptions SourceOnly = new(CompactJson.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitEvidenceBlockers } }
    };

    internal static JsonSerializerOptions For(object report) =>
        report is AnalysisResponse { SourceOnly: true } ? SourceOnly : CompactJson.Options;

    private static void OmitEvidenceBlockers(JsonTypeInfo type)
    {
        if (!AssessmentTypes.Contains(type.Type)) return;
        foreach (var property in type.Properties.Where(property => property.Name == "blockers"))
            property.ShouldSerialize = (_, _) => false;
    }
}
