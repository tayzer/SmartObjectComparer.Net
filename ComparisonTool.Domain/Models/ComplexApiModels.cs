// <copyright file="ComplexApiModels.cs" company="PlaceholderCompany">
using System.Xml.Serialization;

namespace ComparisonTool.Core.Models;

/// <summary>
/// Complex API response model representing an e-commerce order management system
/// This model has deep nesting, multiple collections, and various data types
/// to thoroughly test performance optimizations with ignore rules.
/// </summary>
[XmlRoot(ElementName = "OrderManagementResponse")]
public class ComplexOrderResponse
{
    [XmlElement(ElementName = "RequestId")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    [XmlElement(ElementName = "Timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [XmlElement(ElementName = "ProcessingTime")]
    public TimeSpan ProcessingTime
    {
        get; set;
    }

    [XmlElement(ElementName = "ApiVersion")]
    public string ApiVersion { get; set; } = "2.1.4";

    [XmlElement(ElementName = "ResponseMetadata")]
    public ResponseMetadata Metadata { get; set; } = new ResponseMetadata();

    [XmlElement(ElementName = "OrderData")]
    public OrderData OrderData { get; set; } = new OrderData();

    [XmlArray(ElementName = "ValidationMessages")]
    [XmlArrayItem(ElementName = "Message")]
    public List<ValidationMessage> ValidationMessages { get; set; } = new List<ValidationMessage>();

    [XmlArray(ElementName = "AuditTrail")]
    [XmlArrayItem(ElementName = "AuditEntry")]
    public List<AuditEntry> AuditTrail { get; set; } = new List<AuditEntry>();

    [XmlElement(ElementName = "TestThisThing")]
    public TestThisThing[] TestThisThing { get; set; } = Array.Empty<TestThisThing>();
}

public class TestThisThing
{
    [XmlArray(ElementName = "Tests")]
    [XmlArrayItem(ElementName = "Test")]
    public List<Test>? Tests
    {
        get; set;
    }
}

public class Test
{
    [XmlElement(ElementName = "TestObject")]
    public List<TestObject>? TestObjects
    {
        get; set;
    }
}

public class TestObject
{
    [XmlElement(ElementName = "Id")]
    required public string Id
    {
        get; set;
    }

    [XmlElement(ElementName = "Name")]
    required public string Name
    {
        get; set;
    }

    [XmlElement(ElementName = "Description")]
    required public string Description
    {
        get; set;
    }

    [XmlElement(ElementName = "Type")]
    public string? Type
    {
        get; set;
    }
}

public class ResponseMetadata
{
    [XmlElement(ElementName = "Region", Order = 1)]
    public string Region { get; set; } = "US-EAST-1";

    [XmlElement(ElementName = "Environment", Order = 3)]
    public string Environment { get; set; } = "Production";

    [XmlElement(ElementName = "ServerInfo", Order = 4)]
    public ServerInfo ServerInfo { get; set; } = new ServerInfo();

    [XmlElement(ElementName = "Performance", Order = 5)]
    public PerformanceMetrics Performance { get; set; } = new PerformanceMetrics();

    [XmlArray(ElementName = "Features", Order = 6)]
    [XmlArrayItem(ElementName = "Feature")]
    public List<FeatureFlag> EnabledFeatures { get; set; } = new List<FeatureFlag>();
}

public class ServerInfo
{
    [XmlElement(ElementName = "ServerId")]
    public string ServerId { get; set; } = Environment.MachineName;

    [XmlElement(ElementName = "LoadBalancerGroup")]
    public string LoadBalancerGroup { get; set; } = "Primary";

    [XmlElement(ElementName = "DeploymentVersion")]
    public string DeploymentVersion { get; set; } = "v2.1.4-hotfix.3";

    [XmlElement(ElementName = "MemoryUsage")]
    public long MemoryUsageBytes
    {
        get; set;
    }

    [XmlElement(ElementName = "CpuUsage")]
    public double CpuUsagePercent
    {
        get; set;
    }
}

public class PerformanceMetrics
{
    [XmlElement(ElementName = "DatabaseQueryTime")]
    public TimeSpan DatabaseQueryTime
    {
        get; set;
    }

    [XmlElement(ElementName = "ExternalApiCalls")]
    public int ExternalApiCalls
    {
        get; set;
    }

    [XmlElement(ElementName = "CacheHitRatio")]
    public double CacheHitRatio
    {
        get; set;
    }

    [XmlArray(ElementName = "ComponentTimings")]
    [XmlArrayItem(ElementName = "Timing")]
    public List<ComponentTiming> ComponentTimings { get; set; } = new List<ComponentTiming>();
}

public class ComponentTiming
{
    [XmlElement(ElementName = "ComponentName")]
    public string ComponentName { get; set; } = string.Empty;

    [XmlElement(ElementName = "ExecutionTime")]
    public TimeSpan ExecutionTime
    {
        get; set;
    }

    [XmlElement(ElementName = "CallCount")]
    public int CallCount
    {
        get; set;
    }
}

public class FeatureFlag
{
    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Enabled")]
    public bool Enabled
    {
        get; set;
    }

    [XmlElement(ElementName = "Percentage")]
    public double Percentage
    {
        get; set;
    }
}

public class OrderData
{
    [XmlElement(ElementName = "OrderId")]
    public string OrderId { get; set; } = string.Empty;

    [XmlElement(ElementName = "OrderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    [XmlAttribute(AttributeName = "SourceSystem")]
    public string SourceSystem { get; set; } = string.Empty;

    [XmlElement(ElementName = "Status")]
    public OrderStatus Status
    {
        get; set;
    }

    [XmlElement(ElementName = "Customer")]
    public Customer Customer { get; set; } = new Customer();

    [XmlArray(ElementName = "OrderItems")]
    [XmlArrayItem(ElementName = "Item")]
    public List<OrderItem> Items { get; set; } = new List<OrderItem>();

    [XmlElement(ElementName = "Pricing")]
    public OrderPricing Pricing { get; set; } = new OrderPricing();

    [XmlElement(ElementName = "Fulfillment")]
    public FulfillmentInfo Fulfillment { get; set; } = new FulfillmentInfo();

    [XmlElement(ElementName = "Payment")]
    public PaymentInfo Payment { get; set; } = new PaymentInfo();

    [XmlArray(ElementName = "Promotions")]
    [XmlArrayItem(ElementName = "Promotion")]
    public List<Promotion> AppliedPromotions { get; set; } = new List<Promotion>();

    [XmlArray(ElementName = "OrderHistory")]
    [XmlArrayItem(ElementName = "Event")]
    public List<OrderEvent> OrderHistory { get; set; } = new List<OrderEvent>();
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
    Returned,
}

public class Customer
{
    [XmlElement(ElementName = "CustomerId")]
    public string CustomerId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Profile")]
    public CustomerProfile Profile { get; set; } = new CustomerProfile();

    [XmlElement(ElementName = "Preferences")]
    public CustomerPreferences Preferences { get; set; } = new CustomerPreferences();

    [XmlArray(ElementName = "Addresses")]
    [XmlArrayItem(ElementName = "Address")]
    public List<Address> Addresses { get; set; } = new List<Address>();

    [XmlArray(ElementName = "PaymentMethods")]
    [XmlArrayItem(ElementName = "PaymentMethod")]
    public List<PaymentMethod> PaymentMethods { get; set; } = new List<PaymentMethod>();

    [XmlElement(ElementName = "LoyaltyProgram")]
    public LoyaltyProgram LoyaltyProgram { get; set; } = new LoyaltyProgram();
}

public class CustomerProfile
{
    [XmlElement(ElementName = "FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [XmlElement(ElementName = "LastName")]
    public string LastName { get; set; } = string.Empty;

    [XmlElement(ElementName = "Email")]
    public string Email { get; set; } = string.Empty;

    [XmlElement(ElementName = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [XmlElement(ElementName = "DateOfBirth")]
    public DateTime? DateOfBirth
    {
        get; set;
    }

    [XmlElement(ElementName = "CustomerSince")]
    public DateTime CustomerSince
    {
        get; set;
    }

    [XmlElement(ElementName = "TierLevel")]
    public CustomerTier TierLevel
    {
        get; set;
    }

    [XmlElement(ElementName = "Demographics")]
    public Demographics Demographics { get; set; } = new Demographics();
}

public enum CustomerTier
{
    Bronze,
    Silver,
    Gold,
    Platinum,
    Diamond,
}

public class Demographics
{
    [XmlElement(ElementName = "AgeGroup")]
    public string AgeGroup { get; set; } = string.Empty;

    [XmlElement(ElementName = "Gender")]
    public string Gender { get; set; } = string.Empty;

    [XmlElement(ElementName = "IncomeRange")]
    public string IncomeRange { get; set; } = string.Empty;

    [XmlElement(ElementName = "MaritalStatus")]
    public string MaritalStatus { get; set; } = string.Empty;

    [XmlElement(ElementName = "EducationLevel")]
    public string EducationLevel { get; set; } = string.Empty;
}

public class CustomerPreferences
{
    [XmlElement(ElementName = "CommunicationPreferences")]
    public CommunicationPreferences Communication { get; set; } = new CommunicationPreferences();

    [XmlElement(ElementName = "DeliveryPreferences")]
    public DeliveryPreferences Delivery { get; set; } = new DeliveryPreferences();

    [XmlArray(ElementName = "InterestCategories")]
    [XmlArrayItem(ElementName = "Category")]
    public List<string> InterestCategories { get; set; } = new List<string>();

    [XmlArray(ElementName = "PreviousPurchases")]
    [XmlArrayItem(ElementName = "Purchase")]
    public List<PreviousPurchase> PreviousPurchases { get; set; } = new List<PreviousPurchase>();
}

public class CommunicationPreferences
{
    [XmlElement(ElementName = "EmailNotifications")]
    public bool EmailNotifications
    {
        get; set;
    }

    [XmlElement(ElementName = "SmsNotifications")]
    public bool SmsNotifications
    {
        get; set;
    }

    [XmlElement(ElementName = "PushNotifications")]
    public bool PushNotifications
    {
        get; set;
    }

    [XmlElement(ElementName = "MarketingEmails")]
    public bool MarketingEmails
    {
        get; set;
    }

    [XmlElement(ElementName = "PreferredLanguage")]
    public string PreferredLanguage { get; set; } = "en-US";
}

public class DeliveryPreferences
{
    [XmlElement(ElementName = "PreferredTimeSlot")]
    public string PreferredTimeSlot { get; set; } = string.Empty;

    [XmlElement(ElementName = "DeliveryInstructions")]
    public string DeliveryInstructions { get; set; } = string.Empty;

    [XmlElement(ElementName = "AuthorityToLeave")]
    public bool AuthorityToLeave
    {
        get; set;
    }

    [XmlElement(ElementName = "SignatureRequired")]
    public bool SignatureRequired
    {
        get; set;
    }
}

public class PreviousPurchase
{
    [XmlElement(ElementName = "ProductId")]
    public string ProductId { get; set; } = string.Empty;

    [XmlElement(ElementName = "PurchaseDate")]
    public DateTime PurchaseDate
    {
        get; set;
    }

    [XmlElement(ElementName = "Amount")]
    public decimal Amount
    {
        get; set;
    }

    [XmlElement(ElementName = "Rating")]
    public int? Rating
    {
        get; set;
    }
}

public class Address
{
    [XmlElement(ElementName = "AddressId")]
    public string AddressId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public AddressType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Line1")]
    public string Line1 { get; set; } = string.Empty;

    [XmlElement(ElementName = "Line2")]
    public string Line2 { get; set; } = string.Empty;

    [XmlElement(ElementName = "City")]
    public string City { get; set; } = string.Empty;

    [XmlElement(ElementName = "StateProvince")]
    public string StateProvince { get; set; } = string.Empty;

    [XmlElement(ElementName = "PostalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [XmlElement(ElementName = "Country")]
    public string Country { get; set; } = string.Empty;

    [XmlElement(ElementName = "Coordinates")]
    public GeoCoordinates Coordinates { get; set; } = new GeoCoordinates();

    [XmlElement(ElementName = "IsDefault")]
    public bool IsDefault
    {
        get; set;
    }

    [XmlElement(ElementName = "IsValidated")]
    public bool IsValidated
    {
        get; set;
    }
}

public enum AddressType
{
    Home,
    Work,
    Billing,
    Shipping,
    Other,
}

public class GeoCoordinates
{
    [XmlElement(ElementName = "Latitude")]
    public double Latitude
    {
        get; set;
    }

    [XmlElement(ElementName = "Longitude")]
    public double Longitude
    {
        get; set;
    }

    [XmlElement(ElementName = "Accuracy")]
    public double AccuracyMeters
    {
        get; set;
    }
}

public class PaymentMethod
{
    [XmlElement(ElementName = "PaymentMethodId")]
    public string PaymentMethodId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public PaymentMethodType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "IsDefault")]
    public bool IsDefault
    {
        get; set;
    }

    [XmlElement(ElementName = "CardInfo")]
    public CreditCardInfo CardInfo { get; set; } = new CreditCardInfo();

    [XmlElement(ElementName = "BankAccount")]
    public BankAccountInfo BankAccount { get; set; } = new BankAccountInfo();

    [XmlElement(ElementName = "DigitalWallet")]
    public DigitalWalletInfo DigitalWallet { get; set; } = new DigitalWalletInfo();
}

public enum PaymentMethodType
{
    CreditCard,
    DebitCard,
    BankAccount,
    DigitalWallet,
    GiftCard,
    StoreCredit,
}

public class CreditCardInfo
{
    [XmlElement(ElementName = "Last4Digits")]
    public string Last4Digits { get; set; } = string.Empty;

    [XmlElement(ElementName = "Brand")]
    public string Brand { get; set; } = string.Empty;

    [XmlElement(ElementName = "ExpiryMonth")]
    public int ExpiryMonth
    {
        get; set;
    }

    [XmlElement(ElementName = "ExpiryYear")]
    public int ExpiryYear
    {
        get; set;
    }

    [XmlElement(ElementName = "BillingAddress")]
    public Address BillingAddress { get; set; } = new Address();
}

public class BankAccountInfo
{
    [XmlElement(ElementName = "RoutingNumber")]
    public string RoutingNumber { get; set; } = string.Empty;

    [XmlElement(ElementName = "Last4Digits")]
    public string Last4Digits { get; set; } = string.Empty;

    [XmlElement(ElementName = "AccountType")]
    public string AccountType { get; set; } = string.Empty;

    [XmlElement(ElementName = "BankName")]
    public string BankName { get; set; } = string.Empty;
}

public class DigitalWalletInfo
{
    [XmlElement(ElementName = "Provider")]
    public string Provider { get; set; } = string.Empty;

    [XmlElement(ElementName = "WalletId")]
    public string WalletId { get; set; } = string.Empty;

    [XmlElement(ElementName = "IsVerified")]
    public bool IsVerified
    {
        get; set;
    }
}

public class LoyaltyProgram
{
    [XmlElement(ElementName = "MembershipNumber")]
    public string MembershipNumber { get; set; } = string.Empty;

    [XmlElement(ElementName = "CurrentPoints")]
    public int CurrentPoints
    {
        get; set;
    }

    [XmlElement(ElementName = "TierStatus")]
    public CustomerTier TierStatus
    {
        get; set;
    }

    [XmlElement(ElementName = "NextTierRequirement")]
    public int NextTierRequirement
    {
        get; set;
    }

    [XmlArray(ElementName = "RewardHistory")]
    [XmlArrayItem(ElementName = "Reward")]
    public List<RewardTransaction> RewardHistory { get; set; } = new List<RewardTransaction>();

    [XmlArray(ElementName = "AvailableRewards")]
    [XmlArrayItem(ElementName = "Reward")]
    public List<AvailableReward> AvailableRewards { get; set; } = new List<AvailableReward>();
}

public class RewardTransaction
{
    [XmlElement(ElementName = "TransactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Date")]
    public DateTime Date
    {
        get; set;
    }

    [XmlElement(ElementName = "Points")]
    public int Points
    {
        get; set;
    }

    [XmlElement(ElementName = "Type")]
    public RewardTransactionType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;
}

public enum RewardTransactionType
{
    Earned,
    Redeemed,
    Expired,
    Adjusted,
}

public class AvailableReward
{
    [XmlElement(ElementName = "RewardId")]
    public string RewardId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement(ElementName = "PointsCost")]
    public int PointsCost
    {
        get; set;
    }

    [XmlElement(ElementName = "ExpiryDate")]
    public DateTime? ExpiryDate
    {
        get; set;
    }

    [XmlElement(ElementName = "Category")]
    public string Category { get; set; } = string.Empty;
}

public class OrderItem
{
    [XmlElement(ElementName = "ItemId")]
    public string ItemId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Product")]
    public Product Product { get; set; } = new Product();

    [XmlElement(ElementName = "Quantity")]
    public int Quantity
    {
        get; set;
    }

    [XmlElement(ElementName = "Pricing")]
    public ItemPricing Pricing { get; set; } = new ItemPricing();

    [XmlElement(ElementName = "Customizations")]
    public ProductCustomizations Customizations { get; set; } = new ProductCustomizations();

    [XmlArray(ElementName = "FulfillmentOptions")]
    [XmlArrayItem(ElementName = "Option")]
    public List<FulfillmentOption> FulfillmentOptions { get; set; } = new List<FulfillmentOption>();

    [XmlElement(ElementName = "InventoryInfo")]
    public InventoryInfo InventoryInfo { get; set; } = new InventoryInfo();

    [XmlArray(ElementName = "Warranties")]
    [XmlArrayItem(ElementName = "Warranty")]
    public List<Warranty> Warranties { get; set; } = new List<Warranty>();
}

public class Product
{
    [XmlElement(ElementName = "ProductId")]
    public string ProductId { get; set; } = string.Empty;

    [XmlElement(ElementName = "SKU")]
    public string SKU { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement(ElementName = "Brand")]
    public Brand Brand { get; set; } = new Brand();

    [XmlElement(ElementName = "Category")]
    public ProductCategory Category { get; set; } = new ProductCategory();

    [XmlElement(ElementName = "Specifications")]
    public ProductSpecifications Specifications { get; set; } = new ProductSpecifications();

    [XmlArray(ElementName = "Images")]
    [XmlArrayItem(ElementName = "Image")]
    public List<ProductImage> Images { get; set; } = new List<ProductImage>();

    [XmlArray(ElementName = "Reviews")]
    [XmlArrayItem(ElementName = "Review")]
    public List<ProductReview> Reviews { get; set; } = new List<ProductReview>();

    [XmlElement(ElementName = "Ratings")]
    public ProductRatings Ratings { get; set; } = new ProductRatings();
}

public class Brand
{
    [XmlElement(ElementName = "BrandId")]
    public string BrandId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "LogoUrl")]
    public string LogoUrl { get; set; } = string.Empty;

    [XmlElement(ElementName = "CountryOfOrigin")]
    public string CountryOfOrigin { get; set; } = string.Empty;

    [XmlElement(ElementName = "EstablishedYear")]
    public int? EstablishedYear
    {
        get; set;
    }
}

public class ProductCategory
{
    [XmlElement(ElementName = "CategoryId")]
    public string CategoryId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "ParentCategory")]
    public ProductCategory? ParentCategory
    {
        get; set;
    }

    [XmlArray(ElementName = "SubCategories")]
    [XmlArrayItem(ElementName = "Category")]
    public List<ProductCategory> SubCategories { get; set; } = new List<ProductCategory>();

    [XmlArray(ElementName = "Attributes")]
    [XmlArrayItem(ElementName = "Attribute")]
    public List<CategoryAttribute> Attributes { get; set; } = new List<CategoryAttribute>();
}

public class CategoryAttribute
{
    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Value")]
    public string Value { get; set; } = string.Empty;

    [XmlElement(ElementName = "Unit")]
    public string Unit { get; set; } = string.Empty;

    [XmlElement(ElementName = "IsRequired")]
    public bool IsRequired
    {
        get; set;
    }
}

public class ProductSpecifications
{
    [XmlElement(ElementName = "Weight")]
    public Weight Weight { get; set; } = new Weight();

    [XmlElement(ElementName = "Dimensions")]
    public Dimensions Dimensions { get; set; } = new Dimensions();

    [XmlElement(ElementName = "Material")]
    public string Material { get; set; } = string.Empty;

    [XmlElement(ElementName = "Color")]
    public string Color { get; set; } = string.Empty;

    [XmlElement(ElementName = "Model")]
    public string Model { get; set; } = string.Empty;

    [XmlArray(ElementName = "TechnicalSpecs")]
    [XmlArrayItem(ElementName = "Spec")]
    public List<TechnicalSpecification> TechnicalSpecs { get; set; } = new List<TechnicalSpecification>();

    [XmlArray(ElementName = "Certifications")]
    [XmlArrayItem(ElementName = "Certification")]
    public List<ProductCertification> Certifications { get; set; } = new List<ProductCertification>();
}

public class Weight
{
    [XmlElement(ElementName = "Value")]
    public double Value
    {
        get; set;
    }

    [XmlElement(ElementName = "Unit")]
    public string Unit { get; set; } = "kg";
}

public class Dimensions
{
    [XmlElement(ElementName = "Length")]
    public double Length
    {
        get; set;
    }

    [XmlElement(ElementName = "Width")]
    public double Width
    {
        get; set;
    }

    [XmlElement(ElementName = "Height")]
    public double Height
    {
        get; set;
    }

    [XmlElement(ElementName = "Unit")]
    public string Unit { get; set; } = "cm";
}

public class TechnicalSpecification
{
    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Value")]
    public string Value { get; set; } = string.Empty;

    [XmlElement(ElementName = "Category")]
    public string Category { get; set; } = string.Empty;
}

public class ProductCertification
{
    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "IssuingAuthority")]
    public string IssuingAuthority { get; set; } = string.Empty;

    [XmlElement(ElementName = "CertificationNumber")]
    public string CertificationNumber { get; set; } = string.Empty;

    [XmlElement(ElementName = "ValidUntil")]
    public DateTime? ValidUntil
    {
        get; set;
    }
}

public class ProductImage
{
    [XmlElement(ElementName = "ImageId")]
    public string ImageId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Url")]
    public string Url { get; set; } = string.Empty;

    [XmlElement(ElementName = "AltText")]
    public string AltText { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public ImageType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "SortOrder")]
    public int SortOrder
    {
        get; set;
    }

    [XmlElement(ElementName = "Size")]
    public ImageSize Size { get; set; } = new ImageSize();
}

public enum ImageType
{
    Primary,
    Gallery,
    Thumbnail,
    Zoom,
    Lifestyle,
    Technical,
}

public class ImageSize
{
    [XmlElement(ElementName = "Width")]
    public int Width
    {
        get; set;
    }

    [XmlElement(ElementName = "Height")]
    public int Height
    {
        get; set;
    }

    [XmlElement(ElementName = "FileSizeBytes")]
    public long FileSizeBytes
    {
        get; set;
    }
}

public class ProductReview
{
    [XmlElement(ElementName = "ReviewId")]
    public string ReviewId { get; set; } = string.Empty;

    [XmlElement(ElementName = "ReviewerName")]
    public string ReviewerName { get; set; } = string.Empty;

    [XmlElement(ElementName = "Rating")]
    public int Rating
    {
        get; set;
    }

    [XmlElement(ElementName = "Title")]
    public string Title { get; set; } = string.Empty;

    [XmlElement(ElementName = "Content")]
    public string Content { get; set; } = string.Empty;

    [XmlElement(ElementName = "ReviewDate")]
    public DateTime ReviewDate
    {
        get; set;
    }

    [XmlElement(ElementName = "VerifiedPurchase")]
    public bool VerifiedPurchase
    {
        get; set;
    }

    [XmlElement(ElementName = "HelpfulCount")]
    public int HelpfulCount
    {
        get; set;
    }

    [XmlArray(ElementName = "Images")]
    [XmlArrayItem(ElementName = "Image")]
    public List<string> Images { get; set; } = new List<string>();
}

public class ProductRatings
{
    [XmlElement(ElementName = "AverageRating")]
    public double AverageRating
    {
        get; set;
    }

    [XmlElement(ElementName = "TotalReviews")]
    public int TotalReviews
    {
        get; set;
    }

    [XmlArray(ElementName = "RatingDistribution")]
    [XmlArrayItem(ElementName = "Distribution")]
    public List<RatingDistribution> RatingDistribution { get; set; } = new List<RatingDistribution>();
}

public class RatingDistribution
{
    [XmlElement(ElementName = "Stars")]
    public int Stars
    {
        get; set;
    }

    [XmlElement(ElementName = "Count")]
    public int Count
    {
        get; set;
    }

    [XmlElement(ElementName = "Percentage")]
    public double Percentage
    {
        get; set;
    }
}

public class ItemPricing
{
    [XmlElement(ElementName = "UnitPrice")]
    public decimal UnitPrice
    {
        get; set;
    }

    [XmlElement(ElementName = "DiscountAmount")]
    public decimal DiscountAmount
    {
        get; set;
    }

    [XmlElement(ElementName = "TaxAmount")]
    public decimal TaxAmount
    {
        get; set;
    }

    [XmlElement(ElementName = "TotalPrice")]
    public decimal TotalPrice
    {
        get; set;
    }

    [XmlArray(ElementName = "AppliedDiscounts")]
    [XmlArrayItem(ElementName = "Discount")]
    public List<AppliedDiscount> AppliedDiscounts { get; set; } = new List<AppliedDiscount>();

    [XmlArray(ElementName = "TaxBreakdown")]
    [XmlArrayItem(ElementName = "Tax")]
    public List<TaxBreakdown> TaxBreakdown { get; set; } = new List<TaxBreakdown>();
}

public class AppliedDiscount
{
    [XmlElement(ElementName = "DiscountId")]
    public string DiscountId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public DiscountType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Amount")]
    public decimal Amount
    {
        get; set;
    }

    [XmlElement(ElementName = "Percentage")]
    public double? Percentage
    {
        get; set;
    }
}

public enum DiscountType
{
    Percentage,
    FixedAmount,
    BuyOneGetOne,
    Loyalty,
    Seasonal,
    VolumeDiscount,
}

public class TaxBreakdown
{
    [XmlElement(ElementName = "TaxName")]
    public string TaxName { get; set; } = string.Empty;

    [XmlElement(ElementName = "Rate")]
    public double Rate
    {
        get; set;
    }

    [XmlElement(ElementName = "Amount")]
    public decimal Amount
    {
        get; set;
    }

    [XmlElement(ElementName = "Jurisdiction")]
    public string Jurisdiction { get; set; } = string.Empty;
}

public class ValidationMessage
{
    [XmlElement(ElementName = "MessageId")]
    public string MessageId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Severity")]
    public MessageSeverity Severity
    {
        get; set;
    }

    [XmlElement(ElementName = "Code")]
    public string Code { get; set; } = string.Empty;

    [XmlElement(ElementName = "Message")]
    public string Message { get; set; } = string.Empty;

    [XmlElement(ElementName = "Field")]
    public string Field { get; set; } = string.Empty;

    [XmlElement(ElementName = "Timestamp")]
    public DateTime Timestamp
    {
        get; set;
    }
}

public enum MessageSeverity
{
    Info,
    Warning,
    Error,
    Critical,
}

public class AuditEntry
{
    [XmlElement(ElementName = "EntryId")]
    public string EntryId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Timestamp")]
    public DateTime Timestamp
    {
        get; set;
    }

    [XmlElement(ElementName = "UserId")]
    public string UserId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Action")]
    public string Action { get; set; } = string.Empty;

    [XmlElement(ElementName = "EntityType")]
    public string EntityType { get; set; } = string.Empty;

    [XmlElement(ElementName = "EntityId")]
    public string EntityId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Changes")]
    public string Changes { get; set; } = string.Empty;

    [XmlElement(ElementName = "IPAddress")]
    public string IPAddress { get; set; } = string.Empty;

    [XmlElement(ElementName = "UserAgent")]
    public string UserAgent { get; set; } = string.Empty;
}

public class ProductCustomizations
{
    [XmlArray(ElementName = "Options")]
    [XmlArrayItem(ElementName = "Option")]
    public List<CustomizationOption> Options { get; set; } = new List<CustomizationOption>();

    [XmlElement(ElementName = "PersonalizationText")]
    public string PersonalizationText { get; set; } = string.Empty;

    [XmlElement(ElementName = "GiftWrap")]
    public GiftWrapOption GiftWrap { get; set; } = new GiftWrapOption();
}

public class CustomizationOption
{
    [XmlElement(ElementName = "OptionId")]
    public string OptionId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Value")]
    public string Value { get; set; } = string.Empty;

    [XmlElement(ElementName = "AdditionalCost")]
    public decimal AdditionalCost
    {
        get; set;
    }
}

public class GiftWrapOption
{
    [XmlElement(ElementName = "IsGiftWrap")]
    public bool IsGiftWrap
    {
        get; set;
    }

    [XmlElement(ElementName = "WrapType")]
    public string WrapType { get; set; } = string.Empty;

    [XmlElement(ElementName = "GiftMessage")]
    public string GiftMessage { get; set; } = string.Empty;

    [XmlElement(ElementName = "Cost")]
    public decimal Cost
    {
        get; set;
    }
}

public class FulfillmentOption
{
    [XmlElement(ElementName = "OptionId")]
    public string OptionId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public FulfillmentType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "EstimatedDelivery")]
    public DateTime EstimatedDelivery
    {
        get; set;
    }

    [XmlElement(ElementName = "Cost")]
    public decimal Cost
    {
        get; set;
    }

    [XmlElement(ElementName = "Carrier")]
    public string Carrier { get; set; } = string.Empty;

    [XmlElement(ElementName = "ServiceLevel")]
    public string ServiceLevel { get; set; } = string.Empty;
}

public enum FulfillmentType
{
    StandardDelivery,
    ExpressDelivery,
    NextDayDelivery,
    ClickAndCollect,
    InStorePickup,
}

public class InventoryInfo
{
    [XmlElement(ElementName = "AvailableQuantity")]
    public int AvailableQuantity
    {
        get; set;
    }

    [XmlElement(ElementName = "ReservedQuantity")]
    public int ReservedQuantity
    {
        get; set;
    }

    [XmlElement(ElementName = "WarehouseLocation")]
    public string WarehouseLocation { get; set; } = string.Empty;

    [XmlElement(ElementName = "LastUpdated")]
    public DateTime LastUpdated
    {
        get; set;
    }

    [XmlElement(ElementName = "RestockDate")]
    public DateTime? RestockDate
    {
        get; set;
    }

    [XmlElement(ElementName = "Supplier")]
    public SupplierInfo Supplier { get; set; } = new SupplierInfo();
}

public class SupplierInfo
{
    [XmlElement(ElementName = "SupplierId")]
    public string SupplierId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "LeadTime")]
    public TimeSpan LeadTime
    {
        get; set;
    }

    [XmlElement(ElementName = "ReliabilityScore")]
    public double ReliabilityScore
    {
        get; set;
    }
}

public class Warranty
{
    [XmlElement(ElementName = "WarrantyId")]
    public string WarrantyId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public WarrantyType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Duration")]
    public TimeSpan Duration
    {
        get; set;
    }

    [XmlElement(ElementName = "Coverage")]
    public string Coverage { get; set; } = string.Empty;

    [XmlElement(ElementName = "Cost")]
    public decimal Cost
    {
        get; set;
    }

    [XmlElement(ElementName = "Provider")]
    public string Provider { get; set; } = string.Empty;
}

public enum WarrantyType
{
    Manufacturer,
    Extended,
    ThirdParty,
    Accidental,
}

public class OrderPricing
{
    [XmlElement(ElementName = "Subtotal")]
    public decimal Subtotal
    {
        get; set;
    }

    [XmlElement(ElementName = "TotalDiscount")]
    public decimal TotalDiscount
    {
        get; set;
    }

    [XmlElement(ElementName = "TotalTax")]
    public decimal TotalTax
    {
        get; set;
    }

    [XmlElement(ElementName = "ShippingCost")]
    public decimal ShippingCost
    {
        get; set;
    }

    [XmlElement(ElementName = "HandlingFee")]
    public decimal HandlingFee
    {
        get; set;
    }

    [XmlElement(ElementName = "Total")]
    public decimal Total
    {
        get; set;
    }

    [XmlElement(ElementName = "Currency")]
    public string Currency { get; set; } = "USD";

    [XmlArray(ElementName = "PaymentBreakdown")]
    [XmlArrayItem(ElementName = "Payment")]
    public List<PaymentBreakdown> PaymentBreakdown { get; set; } = new List<PaymentBreakdown>();
}

public class PaymentBreakdown
{
    [XmlElement(ElementName = "Method")]
    public string Method { get; set; } = string.Empty;

    [XmlElement(ElementName = "Amount")]
    public decimal Amount
    {
        get; set;
    }

    [XmlElement(ElementName = "Status")]
    public PaymentStatus Status
    {
        get; set;
    }
}

public enum PaymentStatus
{
    Pending,
    Authorized,
    Captured,
    Failed,
    Refunded,
    PartiallyRefunded,
}

public class FulfillmentInfo
{
    [XmlElement(ElementName = "FulfillmentId")]
    public string FulfillmentId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Status")]
    public FulfillmentStatus Status
    {
        get; set;
    }

    [XmlElement(ElementName = "Method")]
    public FulfillmentMethod Method
    {
        get; set;
    }

    [XmlElement(ElementName = "ShippingAddress")]
    public Address ShippingAddress { get; set; } = new Address();

    [XmlElement(ElementName = "EstimatedDelivery")]
    public DateTime EstimatedDelivery
    {
        get; set;
    }

    [XmlElement(ElementName = "ActualDelivery")]
    public DateTime? ActualDelivery
    {
        get; set;
    }

    [XmlArray(ElementName = "TrackingInfo")]
    [XmlArrayItem(ElementName = "Tracking")]
    public List<TrackingInfo> TrackingInfo { get; set; } = new List<TrackingInfo>();

    [XmlArray(ElementName = "Shipments")]
    [XmlArrayItem(ElementName = "Shipment")]
    public List<Shipment> Shipments { get; set; } = new List<Shipment>();
}

public enum FulfillmentStatus
{
    Pending,
    Processing,
    Shipped,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed,
    Returned,
}

public enum FulfillmentMethod
{
    StandardShipping,
    ExpressShipping,
    OvernightShipping,
    InStorePickup,
    Delivery,
}

public class TrackingInfo
{
    [XmlElement(ElementName = "TrackingNumber")]
    public string TrackingNumber { get; set; } = string.Empty;

    [XmlElement(ElementName = "Carrier")]
    public string Carrier { get; set; } = string.Empty;

    [XmlElement(ElementName = "ServiceType")]
    public string ServiceType { get; set; } = string.Empty;

    [XmlElement(ElementName = "EstimatedDelivery")]
    public DateTime EstimatedDelivery
    {
        get; set;
    }

    [XmlArray(ElementName = "TrackingEvents")]
    [XmlArrayItem(ElementName = "Event")]
    public List<TrackingEvent> TrackingEvents { get; set; } = new List<TrackingEvent>();
}

public class TrackingEvent
{
    [XmlElement(ElementName = "Timestamp")]
    public DateTime Timestamp
    {
        get; set;
    }

    [XmlElement(ElementName = "Location")]
    public string Location { get; set; } = string.Empty;

    [XmlElement(ElementName = "Status")]
    public string Status { get; set; } = string.Empty;

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;
}

public class Shipment
{
    [XmlElement(ElementName = "ShipmentId")]
    public string ShipmentId { get; set; } = string.Empty;

    [XmlElement(ElementName = "CarrierName")]
    public string CarrierName { get; set; } = string.Empty;

    [XmlElement(ElementName = "ServiceLevel")]
    public string ServiceLevel { get; set; } = string.Empty;

    [XmlElement(ElementName = "Weight")]
    public Weight Weight { get; set; } = new Weight();

    [XmlElement(ElementName = "Dimensions")]
    public Dimensions Dimensions { get; set; } = new Dimensions();

    [XmlArray(ElementName = "Items")]
    [XmlArrayItem(ElementName = "Item")]
    public List<ShipmentItem> Items { get; set; } = new List<ShipmentItem>();
}

public class ShipmentItem
{
    [XmlElement(ElementName = "ItemId")]
    public string ItemId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Quantity")]
    public int Quantity
    {
        get; set;
    }

    [XmlElement(ElementName = "Status")]
    public string Status { get; set; } = string.Empty;
}

public class PaymentInfo
{
    [XmlElement(ElementName = "PaymentId")]
    public string PaymentId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Status")]
    public PaymentStatus Status
    {
        get; set;
    }

    [XmlElement(ElementName = "Method")]
    public PaymentMethod Method { get; set; } = new PaymentMethod();

    [XmlElement(ElementName = "AuthorizationCode")]
    public string AuthorizationCode { get; set; } = string.Empty;

    [XmlElement(ElementName = "TransactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [XmlArray(ElementName = "PaymentHistory")]
    [XmlArrayItem(ElementName = "Transaction")]
    public List<PaymentTransaction> PaymentHistory { get; set; } = new List<PaymentTransaction>();

    [XmlElement(ElementName = "FraudCheck")]
    public FraudCheckResult FraudCheck { get; set; } = new FraudCheckResult();
}

public class PaymentTransaction
{
    [XmlElement(ElementName = "TransactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public TransactionType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Amount")]
    public decimal Amount
    {
        get; set;
    }

    [XmlElement(ElementName = "Status")]
    public PaymentStatus Status
    {
        get; set;
    }

    [XmlElement(ElementName = "Timestamp")]
    public DateTime Timestamp
    {
        get; set;
    }

    [XmlElement(ElementName = "Reference")]
    public string Reference { get; set; } = string.Empty;
}

public enum TransactionType
{
    Authorization,
    Capture,
    Refund,
    Void,
}

public class FraudCheckResult
{
    [XmlElement(ElementName = "Score")]
    public double Score
    {
        get; set;
    }

    [XmlElement(ElementName = "RiskLevel")]
    public RiskLevel RiskLevel
    {
        get; set;
    }

    [XmlElement(ElementName = "Decision")]
    public string Decision { get; set; } = string.Empty;

    [XmlArray(ElementName = "Flags")]
    [XmlArrayItem(ElementName = "Flag")]
    public List<FraudFlag> Flags { get; set; } = new List<FraudFlag>();
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

public class FraudFlag
{
    [XmlElement(ElementName = "Type")]
    public string Type { get; set; } = string.Empty;

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement(ElementName = "Severity")]
    public string Severity { get; set; } = string.Empty;
}

public class Promotion
{
    [XmlElement(ElementName = "PromotionId")]
    public string PromotionId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public PromotionType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Code")]
    public string Code { get; set; } = string.Empty;

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement(ElementName = "DiscountAmount")]
    public decimal DiscountAmount
    {
        get; set;
    }

    [XmlElement(ElementName = "DiscountPercentage")]
    public double? DiscountPercentage
    {
        get; set;
    }

    [XmlElement(ElementName = "ValidFrom")]
    public DateTime ValidFrom
    {
        get; set;
    }

    [XmlElement(ElementName = "ValidUntil")]
    public DateTime ValidUntil
    {
        get; set;
    }

    [XmlElement(ElementName = "Terms")]
    public PromotionTerms Terms { get; set; } = new PromotionTerms();
}

public enum PromotionType
{
    PercentageDiscount,
    FixedDiscount,
    FreeShipping,
    BuyOneGetOne,
    VolumeDiscount,
    LoyaltyReward,
}

public class PromotionTerms
{
    [XmlElement(ElementName = "MinOrderValue")]
    public decimal? MinOrderValue
    {
        get; set;
    }

    [XmlElement(ElementName = "MaxDiscount")]
    public decimal? MaxDiscount
    {
        get; set;
    }

    [XmlElement(ElementName = "UsageLimit")]
    public int? UsageLimit
    {
        get; set;
    }

    [XmlElement(ElementName = "CustomerUsageLimit")]
    public int? CustomerUsageLimit
    {
        get; set;
    }

    [XmlArray(ElementName = "EligibleProducts")]
    [XmlArrayItem(ElementName = "ProductId")]
    public List<string> EligibleProducts { get; set; } = new List<string>();

    [XmlArray(ElementName = "ExcludedProducts")]
    [XmlArrayItem(ElementName = "ProductId")]
    public List<string> ExcludedProducts { get; set; } = new List<string>();
}

public class OrderEvent
{
    [XmlElement(ElementName = "EventId")]
    public string EventId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Type")]
    public OrderEventType Type
    {
        get; set;
    }

    [XmlElement(ElementName = "Timestamp")]
    public DateTime Timestamp
    {
        get; set;
    }

    [XmlElement(ElementName = "Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement(ElementName = "UserId")]
    public string UserId { get; set; } = string.Empty;

    [XmlElement(ElementName = "Details")]
    public string Details { get; set; } = string.Empty;

    [XmlElement(ElementName = "SystemGenerated")]
    public bool SystemGenerated
    {
        get; set;
    }
}

public enum OrderEventType
{
    Created,
    Modified,
    Confirmed,
    PaymentProcessed,
    Shipped,
    Delivered,
    Cancelled,
    Returned,
    Refunded,
}
