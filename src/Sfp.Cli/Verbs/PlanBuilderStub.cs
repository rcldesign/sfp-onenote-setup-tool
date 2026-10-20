using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

// Temporary stand-in for Sfp.Core.Plan.PlanBuilder (card M1.6), which another engineer is writing in parallel.
// The integrator deletes this file once the real class is merged: while both exist the compiler reports CS0436
// (a type in this assembly conflicts with the imported type), which under warnings-as-errors fails the build and
// is the signal to remove the stub. GenerateCommand constructs it as new PlanBuilder(dateEngine); adjust that one
// line if the real constructor differs.
namespace Sfp.Core.Plan;

internal sealed class PlanBuilder : IPlanBuilder
{
    public PlanBuilder(IDateEngine dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
    }

    public NotebookPlan Build(Manifest m, Quarter q, GenerateOptions o) =>
        throw new NotImplementedException("The plan builder (card M1.6) has not been merged into this build yet.");
}
