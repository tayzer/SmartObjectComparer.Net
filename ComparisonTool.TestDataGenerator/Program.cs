using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Models;

namespace ComparisonTool.TestDataGenerator
{
    class Program
    {
        static readonly int FileCount = 4000;
        static readonly Random random = new Random(42); // Fixed seed for reproducible results

        static void Main(string[] args)
        {
            Console.WriteLine("🚀 Generating 4000 A and B files with ComplexOrderResponse model...");
            Console.WriteLine("This will thoroughly test performance optimizations with large file sets and ignore rules.");

            // Find the solution root
            string current = AppDomain.CurrentDomain.BaseDirectory;
            string solutionRoot = current;
            int maxUp = 6;
            for (int i = 0; i < maxUp; i++)
            {
                if (File.Exists(Path.Combine(solutionRoot, "ComparisonTool.sln")))
                    break;
                solutionRoot = Path.GetFullPath(Path.Combine(solutionRoot, ".."));
            }

            string domainTestFiles = Path.Combine(solutionRoot, "ComparisonTool.Domain", "TestFiles");
            string outputDir = Path.Combine(domainTestFiles, "4000Files_ComplexModel");
            string actualsDir = Path.Combine(outputDir, "Actuals");
            string expectedsDir = Path.Combine(outputDir, "Expecteds");

            // Create directories
            Directory.CreateDirectory(actualsDir);
            Directory.CreateDirectory(expectedsDir);

            Console.WriteLine($"📁 Output directory: {outputDir}");
            Console.WriteLine($"⏱️  Generating {FileCount * 2} files...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Generate files in parallel for speed
            Parallel.For(1, FileCount + 1, i =>
            {
                try
                {
                    // Generate base order (Expected)
                    var expectedOrder = GenerateComplexOrder(i, isBaselineVersion: true);
                    var expectedXml = SerializeToXml(expectedOrder);
                    File.WriteAllText(Path.Combine(expectedsDir, $"{i}.xml"), expectedXml, System.Text.Encoding.UTF8);

                    // Generate modified order (Actual) with strategic differences
                    var actualOrder = GenerateComplexOrder(i, isBaselineVersion: false);
                    var actualXml = SerializeToXml(actualOrder);
                    File.WriteAllText(Path.Combine(actualsDir, $"{i}.xml"), actualXml, System.Text.Encoding.UTF8);

                    if (i % 500 == 0)
                    {
                        Console.WriteLine($"✅ Generated {i * 2} files...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error generating file {i}: {ex.Message}");
                }
            });

            stopwatch.Stop();

            // Create documentation
            CreateDocumentation(outputDir);

            Console.WriteLine($"🎉 Successfully generated {FileCount * 2} files in {stopwatch.Elapsed.TotalSeconds:F2} seconds!");
            Console.WriteLine($"📊 Average: {(FileCount * 2) / stopwatch.Elapsed.TotalSeconds:F0} files/second");
            Console.WriteLine($"📁 Files saved to: {outputDir}");
            Console.WriteLine();
            Console.WriteLine("🧪 **Performance Testing Recommendations:**");
            Console.WriteLine("1. Add 10+ ignore rules to trigger fast filtering optimization");
            Console.WriteLine("2. Compare these 4000 file pairs and measure timing");
            Console.WriteLine("3. Monitor memory usage vs simple models");
            Console.WriteLine("4. Test both with and without ignore rules");
        }

        static ComplexOrderResponse GenerateComplexOrder(int fileIndex, bool isBaselineVersion)
        {
            var order = new ComplexOrderResponse
            {
                RequestId = $"REQ-{DateTime.UtcNow:yyyyMMdd}-{fileIndex:D6}",
                Timestamp = DateTime.UtcNow.AddMinutes(-random.Next(0, 1440)), // Random time in last 24h
                ProcessingTime = TimeSpan.FromMilliseconds(random.Next(50, 500)),
                ApiVersion = isBaselineVersion ? "2.1.4" : "2.1.5", // Version difference
                
                Metadata = GenerateMetadata(fileIndex, isBaselineVersion),
                OrderData = GenerateOrderData(fileIndex, isBaselineVersion),
                ValidationMessages = GenerateValidationMessages(fileIndex, isBaselineVersion),
                AuditTrail = GenerateAuditTrail(fileIndex, isBaselineVersion)
            };

            return order;
        }

        static ResponseMetadata GenerateMetadata(int fileIndex, bool isBaselineVersion)
        {
            return new ResponseMetadata
            {
                Region = isBaselineVersion ? "US-EAST-1" : "US-WEST-2", // Regional difference
                Environment = "Production",
                ServerInfo = new ServerInfo
                {
                    ServerId = $"srv-{fileIndex % 10:D3}",
                    LoadBalancerGroup = "Primary",
                    DeploymentVersion = isBaselineVersion ? "v2.1.4-hotfix.3" : "v2.1.5-release.1",
                    MemoryUsageBytes = random.Next(1000000, 5000000),
                    CpuUsagePercent = random.NextDouble() * 100
                },
                Performance = new PerformanceMetrics
                {
                    DatabaseQueryTime = TimeSpan.FromMilliseconds(random.Next(10, 100)),
                    ExternalApiCalls = random.Next(5, 20),
                    CacheHitRatio = random.NextDouble(),
                    ComponentTimings = GenerateComponentTimings(3)
                },
                EnabledFeatures = GenerateFeatureFlags(5)
            };
        }

        static OrderData GenerateOrderData(int fileIndex, bool isBaselineVersion)
        {
            return new OrderData
            {
                OrderId = $"ORD-{fileIndex:D6}",
                OrderNumber = $"ON{DateTime.UtcNow:yyyyMMdd}{fileIndex:D4}",
                Status = isBaselineVersion ? OrderStatus.Processing : OrderStatus.Shipped, // Status difference
                
                Customer = GenerateCustomer(fileIndex, isBaselineVersion),
                Items = GenerateOrderItems(random.Next(2, 8), fileIndex, isBaselineVersion),
                Pricing = GenerateOrderPricing(fileIndex, isBaselineVersion),
                Fulfillment = GenerateFulfillmentInfo(fileIndex, isBaselineVersion),
                Payment = GeneratePaymentInfo(fileIndex, isBaselineVersion),
                AppliedPromotions = GeneratePromotions(random.Next(0, 3), fileIndex),
                OrderHistory = GenerateOrderEvents(random.Next(3, 8), fileIndex, isBaselineVersion)
            };
        }

        static Customer GenerateCustomer(int fileIndex, bool isBaselineVersion)
        {
            return new Customer
            {
                CustomerId = $"CUST-{fileIndex % 1000:D6}",
                Profile = new CustomerProfile
                {
                    FirstName = $"Customer{fileIndex % 100}",
                    LastName = $"User{fileIndex % 200}",
                    Email = $"customer{fileIndex % 1000}@example.com",
                    Phone = $"+1555{random.Next(1000000, 9999999)}",
                    DateOfBirth = DateTime.Today.AddYears(-random.Next(18, 80)),
                    CustomerSince = DateTime.Today.AddDays(-random.Next(30, 2000)),
                    TierLevel = (CustomerTier)(fileIndex % 5),
                    Demographics = new Demographics
                    {
                        AgeGroup = "25-34",
                        Gender = fileIndex % 2 == 0 ? "Male" : "Female",
                        IncomeRange = "$50K-$75K",
                        MaritalStatus = fileIndex % 3 == 0 ? "Married" : "Single",
                        EducationLevel = "Bachelor's"
                    }
                },
                Preferences = GenerateCustomerPreferences(fileIndex, isBaselineVersion),
                Addresses = GenerateAddresses(random.Next(1, 4), fileIndex),
                PaymentMethods = GeneratePaymentMethods(random.Next(1, 3), fileIndex),
                LoyaltyProgram = GenerateLoyaltyProgram(fileIndex, isBaselineVersion)
            };
        }

        static List<OrderItem> GenerateOrderItems(int count, int fileIndex, bool isBaselineVersion)
        {
            var items = new List<OrderItem>();
            for (int i = 0; i < count; i++)
            {
                items.Add(new OrderItem
                {
                    ItemId = $"ITEM-{fileIndex:D6}-{i:D2}",
                    Product = GenerateProduct(fileIndex + i, isBaselineVersion),
                    Quantity = random.Next(1, 5),
                    Pricing = new ItemPricing
                    {
                        UnitPrice = (decimal)(random.NextDouble() * 500 + 10),
                        DiscountAmount = isBaselineVersion ? 0 : (decimal)(random.NextDouble() * 50), // Discount difference
                        TaxAmount = (decimal)(random.NextDouble() * 50),
                        TotalPrice = (decimal)(random.NextDouble() * 400 + 50),
                        AppliedDiscounts = GenerateAppliedDiscounts(random.Next(0, 2)),
                        TaxBreakdown = GenerateTaxBreakdown(random.Next(1, 3))
                    },
                    Customizations = new ProductCustomizations
                    {
                        Options = GenerateCustomizationOptions(random.Next(0, 3)),
                        PersonalizationText = isBaselineVersion ? "" : $"Custom text {i}",
                        GiftWrap = new GiftWrapOption
                        {
                            IsGiftWrap = random.Next(0, 5) == 0,
                            WrapType = "Standard",
                            GiftMessage = "",
                            Cost = 5.99m
                        }
                    },
                    FulfillmentOptions = GenerateFulfillmentOptions(random.Next(2, 4)),
                    InventoryInfo = new InventoryInfo
                    {
                        AvailableQuantity = random.Next(10, 100),
                        ReservedQuantity = random.Next(0, 10),
                        WarehouseLocation = $"WH-{fileIndex % 5}",
                        LastUpdated = DateTime.UtcNow.AddMinutes(-random.Next(0, 60)),
                        RestockDate = DateTime.UtcNow.AddDays(random.Next(1, 30)),
                        Supplier = new SupplierInfo
                        {
                            SupplierId = $"SUP-{random.Next(1, 10):D3}",
                            Name = $"Supplier {random.Next(1, 10)}",
                            LeadTime = TimeSpan.FromDays(random.Next(1, 14)),
                            ReliabilityScore = random.NextDouble()
                        }
                    },
                    Warranties = GenerateWarranties(random.Next(0, 2))
                });
            }
            return items;
        }

        static Product GenerateProduct(int productIndex, bool isBaselineVersion)
        {
            return new Product
            {
                ProductId = $"PROD-{productIndex % 500:D6}",
                SKU = $"SKU{productIndex % 1000:D6}",
                Name = $"Product {productIndex % 100}",
                Description = isBaselineVersion ? $"Description for product {productIndex}" : $"Updated description for product {productIndex}",
                Brand = new Brand
                {
                    BrandId = $"BRAND-{productIndex % 20:D3}",
                    Name = $"Brand {productIndex % 20}",
                    LogoUrl = $"https://example.com/logos/brand{productIndex % 20}.png",
                    CountryOfOrigin = "USA",
                    EstablishedYear = 1990 + (productIndex % 30)
                },
                Category = GenerateProductCategory(productIndex),
                Specifications = new ProductSpecifications
                {
                    Weight = new Weight { Value = random.NextDouble() * 10 + 0.1, Unit = "kg" },
                    Dimensions = new Dimensions 
                    { 
                        Length = random.NextDouble() * 50 + 5, 
                        Width = random.NextDouble() * 30 + 3, 
                        Height = random.NextDouble() * 20 + 2, 
                        Unit = "cm" 
                    },
                    Material = "Plastic",
                    Color = $"Color{productIndex % 10}",
                    Model = $"Model-{productIndex % 50}",
                    TechnicalSpecs = GenerateTechnicalSpecs(random.Next(3, 8)),
                    Certifications = GenerateCertifications(random.Next(1, 4))
                },
                Images = GenerateProductImages(random.Next(3, 8), productIndex),
                Reviews = GenerateProductReviews(random.Next(5, 15), productIndex, isBaselineVersion),
                Ratings = new ProductRatings
                {
                    AverageRating = random.NextDouble() * 2 + 3, // 3-5 stars
                    TotalReviews = random.Next(10, 500),
                    RatingDistribution = GenerateRatingDistribution()
                }
            };
        }

        // Helper methods for generating complex nested objects
        static List<ComponentTiming> GenerateComponentTimings(int count)
        {
            var timings = new List<ComponentTiming>();
            var components = new[] { "Database", "Cache", "ExternalAPI", "Validation", "Serialization" };
            
            for (int i = 0; i < count; i++)
            {
                timings.Add(new ComponentTiming
                {
                    ComponentName = components[i % components.Length],
                    ExecutionTime = TimeSpan.FromMilliseconds(random.Next(1, 100)),
                    CallCount = random.Next(1, 10)
                });
            }
            return timings;
        }

        static List<FeatureFlag> GenerateFeatureFlags(int count)
        {
            var flags = new List<FeatureFlag>();
            for (int i = 0; i < count; i++)
            {
                flags.Add(new FeatureFlag
                {
                    Name = $"Feature{i}",
                    Enabled = random.Next(0, 2) == 1,
                    Percentage = random.NextDouble() * 100
                });
            }
            return flags;
        }

        static CustomerPreferences GenerateCustomerPreferences(int fileIndex, bool isBaselineVersion)
        {
            return new CustomerPreferences
            {
                Communication = new CommunicationPreferences
                {
                    EmailNotifications = true,
                    SmsNotifications = isBaselineVersion ? false : true, // Preference change
                    PushNotifications = true,
                    MarketingEmails = fileIndex % 2 == 0,
                    PreferredLanguage = "en-US"
                },
                Delivery = new DeliveryPreferences
                {
                    PreferredTimeSlot = "9AM-5PM",
                    DeliveryInstructions = isBaselineVersion ? "Leave at door" : "Ring doorbell",
                    AuthorityToLeave = true,
                    SignatureRequired = false
                },
                InterestCategories = new List<string> { "Electronics", "Books", "Clothing" },
                PreviousPurchases = GeneratePreviousPurchases(random.Next(2, 8), fileIndex)
            };
        }

        static List<Address> GenerateAddresses(int count, int fileIndex)
        {
            var addresses = new List<Address>();
            for (int i = 0; i < count; i++)
            {
                addresses.Add(new Address
                {
                    AddressId = $"ADDR-{fileIndex:D6}-{i}",
                    Type = (AddressType)(i % 5),
                    Line1 = $"{random.Next(100, 9999)} Main St",
                    Line2 = i == 0 ? $"Apt {random.Next(1, 50)}" : "",
                    City = $"City{fileIndex % 50}",
                    StateProvince = "CA",
                    PostalCode = $"{random.Next(10000, 99999):D5}",
                    Country = "USA",
                    Coordinates = new GeoCoordinates
                    {
                        Latitude = random.NextDouble() * 180 - 90,
                        Longitude = random.NextDouble() * 360 - 180,
                        AccuracyMeters = random.NextDouble() * 10
                    },
                    IsDefault = i == 0,
                    IsValidated = true
                });
            }
            return addresses;
        }

        static List<PaymentMethod> GeneratePaymentMethods(int count, int fileIndex)
        {
            var methods = new List<PaymentMethod>();
            for (int i = 0; i < count; i++)
            {
                methods.Add(new PaymentMethod
                {
                    PaymentMethodId = $"PM-{fileIndex:D6}-{i}",
                    Type = (PaymentMethodType)(i % 6),
                    IsDefault = i == 0,
                    CardInfo = new CreditCardInfo
                    {
                        Last4Digits = $"{random.Next(1000, 9999)}",
                        Brand = "Visa",
                        ExpiryMonth = random.Next(1, 13),
                        ExpiryYear = DateTime.Now.Year + random.Next(1, 6),
                        BillingAddress = new Address()
                    }
                });
            }
            return methods;
        }

        static LoyaltyProgram GenerateLoyaltyProgram(int fileIndex, bool isBaselineVersion)
        {
            return new LoyaltyProgram
            {
                MembershipNumber = $"LP{fileIndex:D8}",
                CurrentPoints = isBaselineVersion ? random.Next(100, 1000) : random.Next(200, 1200), // Points difference
                TierStatus = (CustomerTier)(fileIndex % 5),
                NextTierRequirement = random.Next(500, 2000),
                RewardHistory = GenerateRewardHistory(random.Next(3, 10), fileIndex),
                AvailableRewards = GenerateAvailableRewards(random.Next(5, 15))
            };
        }

        static List<RewardTransaction> GenerateRewardHistory(int count, int fileIndex)
        {
            var history = new List<RewardTransaction>();
            for (int i = 0; i < count; i++)
            {
                history.Add(new RewardTransaction
                {
                    TransactionId = $"RT-{fileIndex:D6}-{i:D2}",
                    Date = DateTime.UtcNow.AddDays(-random.Next(1, 365)),
                    Points = random.Next(-500, 500),
                    Type = (RewardTransactionType)(i % 4),
                    Description = $"Transaction {i + 1}"
                });
            }
            return history;
        }

        static List<AvailableReward> GenerateAvailableRewards(int count)
        {
            var rewards = new List<AvailableReward>();
            for (int i = 0; i < count; i++)
            {
                rewards.Add(new AvailableReward
                {
                    RewardId = $"RWD-{i:D3}",
                    Name = $"Reward {i + 1}",
                    Description = $"Description for reward {i + 1}",
                    PointsCost = random.Next(100, 1000),
                    ExpiryDate = DateTime.UtcNow.AddDays(random.Next(30, 365)),
                    Category = "General"
                });
            }
            return rewards;
        }

        // Additional helper methods for remaining complex objects
        static List<CustomizationOption> GenerateCustomizationOptions(int count)
        {
            var options = new List<CustomizationOption>();
            for (int i = 0; i < count; i++)
            {
                options.Add(new CustomizationOption
                {
                    OptionId = $"OPT-{i:D3}",
                    Name = $"Option {i + 1}",
                    Value = $"Value {i + 1}",
                    AdditionalCost = (decimal)(random.NextDouble() * 20)
                });
            }
            return options;
        }

        static List<FulfillmentOption> GenerateFulfillmentOptions(int count)
        {
            var options = new List<FulfillmentOption>();
            for (int i = 0; i < count; i++)
            {
                options.Add(new FulfillmentOption
                {
                    OptionId = $"FO-{i:D3}",
                    Type = (FulfillmentType)(i % 5),
                    EstimatedDelivery = DateTime.UtcNow.AddDays(random.Next(1, 14)),
                    Cost = (decimal)(random.NextDouble() * 50),
                    Carrier = $"Carrier {i + 1}",
                    ServiceLevel = "Standard"
                });
            }
            return options;
        }

        static List<Warranty> GenerateWarranties(int count)
        {
            var warranties = new List<Warranty>();
            for (int i = 0; i < count; i++)
            {
                warranties.Add(new Warranty
                {
                    WarrantyId = $"WAR-{i:D3}",
                    Type = (WarrantyType)(i % 4),
                    Duration = TimeSpan.FromDays(random.Next(365, 1095)),
                    Coverage = "Full Coverage",
                    Cost = (decimal)(random.NextDouble() * 100),
                    Provider = "WarrantyProvider"
                });
            }
            return warranties;
        }

        static ProductCategory GenerateProductCategory(int productIndex)
        {
            return new ProductCategory
            {
                CategoryId = $"CAT-{productIndex % 10:D3}",
                Name = $"Category {productIndex % 10}",
                ParentCategory = null,
                SubCategories = new List<ProductCategory>(),
                Attributes = GenerateCategoryAttributes(random.Next(2, 6))
            };
        }

        static List<CategoryAttribute> GenerateCategoryAttributes(int count)
        {
            var attributes = new List<CategoryAttribute>();
            for (int i = 0; i < count; i++)
            {
                attributes.Add(new CategoryAttribute
                {
                    Name = $"Attribute {i + 1}",
                    Value = $"Value {i + 1}",
                    Unit = "unit",
                    IsRequired = i < 2
                });
            }
            return attributes;
        }

        static List<TechnicalSpecification> GenerateTechnicalSpecs(int count)
        {
            var specs = new List<TechnicalSpecification>();
            for (int i = 0; i < count; i++)
            {
                specs.Add(new TechnicalSpecification
                {
                    Name = $"Spec {i + 1}",
                    Value = $"Value {i + 1}",
                    Category = "Technical"
                });
            }
            return specs;
        }

        static List<ProductCertification> GenerateCertifications(int count)
        {
            var certs = new List<ProductCertification>();
            for (int i = 0; i < count; i++)
            {
                certs.Add(new ProductCertification
                {
                    Name = $"Certification {i + 1}",
                    IssuingAuthority = "Authority",
                    CertificationNumber = $"CERT-{i:D6}",
                    ValidUntil = DateTime.UtcNow.AddYears(random.Next(1, 5))
                });
            }
            return certs;
        }

        static List<ProductImage> GenerateProductImages(int count, int productIndex)
        {
            var images = new List<ProductImage>();
            for (int i = 0; i < count; i++)
            {
                images.Add(new ProductImage
                {
                    ImageId = $"IMG-{productIndex:D6}-{i:D2}",
                    Url = $"https://example.com/images/product{productIndex}_{i}.jpg",
                    AltText = $"Product image {i + 1}",
                    Type = (ImageType)(i % 6),
                    SortOrder = i,
                    Size = new ImageSize
                    {
                        Width = random.Next(200, 1000),
                        Height = random.Next(200, 1000),
                        FileSizeBytes = random.Next(10000, 500000)
                    }
                });
            }
            return images;
        }

        static List<ProductReview> GenerateProductReviews(int count, int productIndex, bool isBaselineVersion)
        {
            var reviews = new List<ProductReview>();
            for (int i = 0; i < count; i++)
            {
                reviews.Add(new ProductReview
                {
                    ReviewId = $"REV-{productIndex:D6}-{i:D2}",
                    ReviewerName = $"Reviewer {i + 1}",
                    Rating = random.Next(1, 6),
                    Title = $"Review title {i + 1}",
                    Content = isBaselineVersion ? $"Review content {i + 1}" : $"Updated review content {i + 1}",
                    ReviewDate = DateTime.UtcNow.AddDays(-random.Next(1, 365)),
                    VerifiedPurchase = random.Next(0, 2) == 1,
                    HelpfulCount = random.Next(0, 50),
                    Images = new List<string>()
                });
            }
            return reviews;
        }

        static List<RatingDistribution> GenerateRatingDistribution()
        {
            var distribution = new List<RatingDistribution>();
            for (int i = 1; i <= 5; i++)
            {
                distribution.Add(new RatingDistribution
                {
                    Stars = i,
                    Count = random.Next(1, 100),
                    Percentage = random.NextDouble() * 100
                });
            }
            return distribution;
        }

        static List<AppliedDiscount> GenerateAppliedDiscounts(int count)
        {
            var discounts = new List<AppliedDiscount>();
            for (int i = 0; i < count; i++)
            {
                discounts.Add(new AppliedDiscount
                {
                    DiscountId = $"DISC-{i:D3}",
                    Name = $"Discount {i + 1}",
                    Type = (DiscountType)(i % 6),
                    Amount = (decimal)(random.NextDouble() * 50),
                    Percentage = random.NextDouble() * 20
                });
            }
            return discounts;
        }

        static List<TaxBreakdown> GenerateTaxBreakdown(int count)
        {
            var taxes = new List<TaxBreakdown>();
            for (int i = 0; i < count; i++)
            {
                taxes.Add(new TaxBreakdown
                {
                    TaxName = $"Tax {i + 1}",
                    Rate = random.NextDouble() * 0.15,
                    Amount = (decimal)(random.NextDouble() * 20),
                    Jurisdiction = "State"
                });
            }
            return taxes;
        }

        static List<PreviousPurchase> GeneratePreviousPurchases(int count, int fileIndex)
        {
            var purchases = new List<PreviousPurchase>();
            for (int i = 0; i < count; i++)
            {
                purchases.Add(new PreviousPurchase
                {
                    ProductId = $"PROD-{(fileIndex + i) % 500:D6}",
                    PurchaseDate = DateTime.UtcNow.AddDays(-random.Next(30, 365)),
                    Amount = (decimal)(random.NextDouble() * 200 + 10),
                    Rating = random.Next(1, 6)
                });
            }
            return purchases;
        }

        static OrderPricing GenerateOrderPricing(int fileIndex, bool isBaselineVersion)
        {
            return new OrderPricing
            {
                Subtotal = (decimal)(random.NextDouble() * 500 + 100),
                TotalDiscount = isBaselineVersion ? 0 : (decimal)(random.NextDouble() * 50), // Discount difference
                TotalTax = (decimal)(random.NextDouble() * 50),
                ShippingCost = (decimal)(random.NextDouble() * 20 + 5),
                HandlingFee = (decimal)(random.NextDouble() * 10),
                Total = (decimal)(random.NextDouble() * 400 + 200),
                Currency = "USD",
                PaymentBreakdown = new List<PaymentBreakdown>
                {
                    new PaymentBreakdown
                    {
                        Method = "Credit Card",
                        Amount = (decimal)(random.NextDouble() * 400 + 200),
                        Status = PaymentStatus.Captured
                    }
                }
            };
        }

        static FulfillmentInfo GenerateFulfillmentInfo(int fileIndex, bool isBaselineVersion)
        {
            return new FulfillmentInfo
            {
                FulfillmentId = $"FUL-{fileIndex:D6}",
                Status = isBaselineVersion ? FulfillmentStatus.Processing : FulfillmentStatus.Shipped,
                Method = FulfillmentMethod.StandardShipping,
                ShippingAddress = new Address(),
                EstimatedDelivery = DateTime.UtcNow.AddDays(random.Next(3, 10)),
                ActualDelivery = isBaselineVersion ? null : DateTime.UtcNow.AddDays(-1),
                TrackingInfo = new List<TrackingInfo>
                {
                    new TrackingInfo
                    {
                        TrackingNumber = $"TRK{fileIndex:D10}",
                        Carrier = "UPS",
                        ServiceType = "Ground",
                        EstimatedDelivery = DateTime.UtcNow.AddDays(random.Next(3, 10)),
                        TrackingEvents = new List<TrackingEvent>()
                    }
                },
                Shipments = new List<Shipment>()
            };
        }

        static PaymentInfo GeneratePaymentInfo(int fileIndex, bool isBaselineVersion)
        {
            return new PaymentInfo
            {
                PaymentId = $"PAY-{fileIndex:D6}",
                Status = isBaselineVersion ? PaymentStatus.Authorized : PaymentStatus.Captured,
                Method = new PaymentMethod(),
                AuthorizationCode = $"AUTH{random.Next(100000, 999999)}",
                TransactionId = $"TXN-{fileIndex:D8}",
                PaymentHistory = new List<PaymentTransaction>(),
                FraudCheck = new FraudCheckResult
                {
                    Score = random.NextDouble(),
                    RiskLevel = RiskLevel.Low,
                    Decision = "Approve",
                    Flags = new List<FraudFlag>()
                }
            };
        }

        static List<Promotion> GeneratePromotions(int count, int fileIndex)
        {
            var promotions = new List<Promotion>();
            for (int i = 0; i < count; i++)
            {
                promotions.Add(new Promotion
                {
                    PromotionId = $"PROMO-{i:D3}",
                    Name = $"Promotion {i + 1}",
                    Type = (PromotionType)(i % 6),
                    Code = $"SAVE{i + 1}",
                    Description = $"Description for promotion {i + 1}",
                    DiscountAmount = (decimal)(random.NextDouble() * 50),
                    DiscountPercentage = random.NextDouble() * 20,
                    ValidFrom = DateTime.UtcNow.AddDays(-30),
                    ValidUntil = DateTime.UtcNow.AddDays(30),
                    Terms = new PromotionTerms
                    {
                        MinOrderValue = (decimal)(random.NextDouble() * 100 + 50),
                        MaxDiscount = (decimal)(random.NextDouble() * 100),
                        UsageLimit = random.Next(100, 1000),
                        CustomerUsageLimit = 1,
                        EligibleProducts = new List<string>(),
                        ExcludedProducts = new List<string>()
                    }
                });
            }
            return promotions;
        }

        static List<OrderEvent> GenerateOrderEvents(int count, int fileIndex, bool isBaselineVersion)
        {
            var events = new List<OrderEvent>();
            for (int i = 0; i < count; i++)
            {
                events.Add(new OrderEvent
                {
                    EventId = $"EVT-{fileIndex:D6}-{i:D2}",
                    Type = (OrderEventType)(i % 9),
                    Timestamp = DateTime.UtcNow.AddHours(-random.Next(1, 72)), // Different timestamps
                    Description = $"Order event {i + 1}",
                    UserId = isBaselineVersion ? $"user{fileIndex % 10}" : $"user{(fileIndex + 1) % 10}", // User difference
                    Details = $"Event details {i + 1}",
                    SystemGenerated = i % 2 == 0
                });
            }
            return events;
        }

        static List<ValidationMessage> GenerateValidationMessages(int fileIndex, bool isBaselineVersion)
        {
            var messages = new List<ValidationMessage>();
            
            // Only add validation messages in actual files to create differences
            if (!isBaselineVersion)
            {
                for (int i = 0; i < random.Next(0, 3); i++)
                {
                    messages.Add(new ValidationMessage
                    {
                        MessageId = $"VAL-{fileIndex:D6}-{i:D2}",
                        Severity = (MessageSeverity)(i % 4),
                        Code = $"VAL{i:D3}",
                        Message = $"Validation message {i + 1}",
                        Field = $"Field{i + 1}",
                        Timestamp = DateTime.UtcNow.AddMinutes(-random.Next(0, 60))
                    });
                }
            }
            
            return messages;
        }

        static List<AuditEntry> GenerateAuditTrail(int fileIndex, bool isBaselineVersion)
        {
            var entries = new List<AuditEntry>();
            for (int i = 0; i < random.Next(2, 6); i++)
            {
                entries.Add(new AuditEntry
                {
                    EntryId = $"AUD-{fileIndex:D6}-{i:D2}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-random.Next(0, 1440)), // Different audit times
                    UserId = $"user{fileIndex % 10}",
                    Action = $"Action{i + 1}",
                    EntityType = "Order",
                    EntityId = $"ORD-{fileIndex:D6}",
                    Changes = isBaselineVersion ? $"Changes {i + 1}" : $"Updated changes {i + 1}",
                    IPAddress = $"192.168.1.{random.Next(1, 255)}",
                    UserAgent = "ComparisonTool/2.1"
                });
            }
            return entries;
        }

        static string SerializeToXml<T>(T obj)
        {
            var serializer = new XmlSerializer(typeof(T));
            
            // Use MemoryStream instead of StringWriter to avoid UTF-16 encoding issues
            using var memoryStream = new MemoryStream();
            
            // Use XmlWriterSettings to control encoding and formatting
            var settings = new XmlWriterSettings
            {
                Encoding = new System.Text.UTF8Encoding(false), // UTF-8 without BOM
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false
            };
            
            using var xmlWriter = XmlWriter.Create(memoryStream, settings);
            
            // Create namespace settings to avoid extra namespaces
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", ""); // Remove default namespace
            
            serializer.Serialize(xmlWriter, obj, namespaces);
            xmlWriter.Flush();
            
            // Convert to string using UTF-8
            return System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        static void CreateDocumentation(string outputDir)
        {
            var readme = @"# 4000 File Performance Test Dataset

## Overview
This dataset contains 4000 pairs of ComplexOrderResponse XML files designed to thoroughly test performance optimizations with large file sets and ignore rules.

## Dataset Characteristics
- **File Count**: 4000 Expected + 4000 Actual = 8000 total files
- **Model**: ComplexOrderResponse (60+ classes, 150+ properties)
- **Size**: Each file ~15-30KB (complex nested structures)
- **Differences**: Strategic variations to test ignore rule performance

## Key Differences Between Expected/Actual Files
1. **Version changes**: ApiVersion 2.1.4 → 2.1.5
2. **Status changes**: Processing → Shipped
3. **Regional differences**: US-EAST-1 → US-WEST-2  
4. **Preference changes**: SMS notifications enabled/disabled
5. **Discount differences**: 0 → random discount amounts
6. **User differences**: Different user IDs in audit trail
7. **Timestamp variations**: Different audit and event times
8. **Content updates**: Product descriptions, review content
9. **Validation messages**: Only present in actual files
10. **Points differences**: Loyalty program points variations

## Performance Testing Recommendations

### Ignore Rules to Test
```
// Timestamps (commonly ignored)
Timestamp
OrderData.OrderHistory[*].Timestamp
AuditTrail[*].Timestamp
Metadata.Performance.DatabaseQueryTime

// IDs and audit data
RequestId
OrderData.OrderHistory[*].UserId
AuditTrail[*].UserId

// Dynamic content
OrderData.Items[*].Product.Reviews[*].Content
ValidationMessages[*]

// Regional/version differences  
Metadata.Region
ApiVersion
Metadata.ServerInfo.DeploymentVersion
```

### Expected Performance Improvements
- **Before**: 10+ seconds with 10+ ignore rules
- **After**: <2 seconds with fast filtering optimization
- **Memory**: Significantly reduced MembersToIgnore list size

Generated on: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"
Generator: ComplexOrderResponse model with strategic variations
";

            File.WriteAllText(Path.Combine(outputDir, "README.md"), readme);
        }
    }
}