using System;

namespace ParvNegar.Domain
{
    /// <summary>
    /// School identity, separate from the product (ParvNegar) and the publisher (OAR NOVA).
    /// The home title is stored and displayed exactly as configured: no trimming,
    /// no character normalization and no space changes (SRS §1, SCH-01).
    /// </summary>
    public sealed class SchoolProfile
    {
        public const int MaxTitleLength = 300;

        public SchoolProfile(Guid id, string schoolName, string homeTitle, string logoAssetId)
        {
            Guard.NotEmpty(id, nameof(id));
            Id = id;
            SchoolName = Guard.RequiredText(schoolName, nameof(schoolName), 200);
            HomeTitle = Guard.RequiredText(homeTitle, nameof(homeTitle), MaxTitleLength);
            LogoAssetId = Guard.OptionalText(logoAssetId, nameof(logoAssetId), 100);
        }

        public Guid Id { get; }
        public string SchoolName { get; }

        /// <summary>Exact owner-approved title. Compared ordinally; never normalized.</summary>
        public string HomeTitle { get; }

        public string LogoAssetId { get; }
    }
}
