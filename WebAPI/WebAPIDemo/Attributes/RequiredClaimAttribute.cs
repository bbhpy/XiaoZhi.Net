namespace WebAPIDemo.Attributes
{
    public class RequiredClaimAttribute : Attribute
    {
        public string ClaimType { get; }
        public string ClaimValue { get; }

        public RequiredClaimAttribute(string claimType, string claimValue)
        {
            this.ClaimType = claimType;
            this.ClaimValue = claimValue;
        }
    }
}
