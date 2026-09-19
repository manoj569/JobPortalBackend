namespace JobPortal.Application.Abstractions.Jobs;

public interface IExternalJobNormalizer
{
    RawExternalJob Normalize(RawExternalJob rawJob);
}
