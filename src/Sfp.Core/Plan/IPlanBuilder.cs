using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Core.Plan;

/// <summary>Walks the manifest's notebook plan and the quarter to produce the full <see cref="NotebookPlan"/>, links included.</summary>
public interface IPlanBuilder
{
    NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o);
}
