using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

public sealed class ForceTranslateRequest
{
    public int MediaId { get; set; }
    public MediaType MediaType { get; set; }
    public bool Overwrite { get; set; }
}
