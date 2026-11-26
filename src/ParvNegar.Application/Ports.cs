using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ParvNegar.Domain;

namespace ParvNegar.Application
{
    /// <summary>Supplies the school identity shown on the home screen (never hard-coded in views).</summary>
    public interface ISchoolProfileSource
    {
        /// <summary>Returns null when no valid profile is configured.</summary>
        SchoolProfile Load();
    }

    /// <summary>Product identity, kept separate from school and publisher identity (SRS UI-01, AC-04).</summary>
    public sealed class ProductIdentity
    {
        public const string ProductNameFa = "پرونگار";
        public const string ProductNameEn = "ParvNegar";
        public const string PublisherName = "OAR NOVA Games Studio";
        public const string PublisherSite = "https://oarnova.games";
        public const string DistributionSite = "https://yarizan.oarnova.games";
    }

    /// <summary>
    /// PDF/print rendering port. Implementation is deferred (PDF engine selection deferred beyond
    /// Phase 1, decision D-06/L-01). The application must start without any renderer installed.
    /// </summary>
    public interface IReportRenderer
    {
        bool IsAvailable { get; }
        string UnavailableReason { get; }
        Task RenderPdfAsync(string reportKind, Guid enrollmentId, Stream destination, CancellationToken cancellationToken);
    }

    /// <summary>In-app safe document preview port (DOC-04). Deferred to Phase 5; optional at startup.</summary>
    public interface IDocumentPreviewer
    {
        bool IsAvailable { get; }
        bool CanPreview(string contentType);
    }

    /// <summary>Default used until an engine is approved: reports "not available" instead of failing startup.</summary>
    public sealed class UnavailableReportRenderer : IReportRenderer
    {
        public bool IsAvailable => false;
        public string UnavailableReason => "PDF engine not selected (deferred decision D-06).";
        public Task RenderPdfAsync(string reportKind, Guid enrollmentId, Stream destination, CancellationToken cancellationToken)
            => throw new NotSupportedException(UnavailableReason);
    }

    public sealed class UnavailableDocumentPreviewer : IDocumentPreviewer
    {
        public bool IsAvailable => false;
        public bool CanPreview(string contentType) => false;
    }
}
