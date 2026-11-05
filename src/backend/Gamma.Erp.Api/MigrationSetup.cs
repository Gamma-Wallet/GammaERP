using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Employees;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Migration;

namespace Gamma.Erp.Api;

/// <summary>The entities the Migration menu reaches, with the same rules as their own endpoints.</summary>
public static class MigrationSetup
{
    public static MigrationEntities Entities(IServiceProvider services)
    {
        var db = services.GetRequiredService<ErpDbContext>();
        var request = services.GetRequiredService<RequestContext>();
        var visibility = services.GetRequiredService<Visibility>();
        return new(new Dictionary<string, IEntityRows>(StringComparer.Ordinal)
        {
            ["documents"] = services.GetRequiredService<Gamma.Erp.Services.Documents.DocumentService>().Entities,
            ["transactions"] = new EntityService<TransactionCategory, TransactionType, InterTransactionTypeCategory, TransactionTypeInfo, TransactionTypeInfoChoice, Transaction, TransactionInfo, InterTransactionCategory, TransactionAccess>(db, request, visibility),
            ["charts"] = new EntityService<ChartCategory, ChartType, InterChartTypeCategory, ChartTypeInfo, ChartTypeInfoChoice, Chart, ChartInfo, InterChartCategory, ChartAccess>(db, request, visibility),
            ["partners"] = new EntityService<PartnerCategory, PartnerType, InterPartnerTypeCategory, PartnerTypeInfo, PartnerTypeInfoChoice, Partner, PartnerInfo, InterPartnerCategory, PartnerAccess>(db, request, visibility),
            ["products"] = new EntityService<ProductCategory, ProductType, InterProductTypeCategory, ProductTypeInfo, ProductTypeInfoChoice, Product, ProductInfo, InterProductCategory, ProductAccess>(db, request, visibility),
            ["cost-centers"] = new EntityService<CostCenterCategory, CostCenterType, InterCostCenterTypeCategory, CostCenterTypeInfo, CostCenterTypeInfoChoice, CostCenter, CostCenterInfo, InterCostCenterCategory, CostCenterAccess>(db, request, visibility)
                { Check = CostCenterRules.CompanyLevel(db) },
            ["units"] = new EntityService<UnitCategory, UnitType, InterUnitTypeCategory, UnitTypeInfo, UnitTypeInfoChoice, Unit, UnitInfo, InterUnitCategory, UnitAccess>(db, request, visibility),
            ["employees"] = new EntityService<EmployeeCategory, EmployeeType, InterEmployeeTypeCategory, EmployeeTypeInfo, EmployeeTypeInfoChoice, Employee, EmployeeInfo, InterEmployeeCategory, EmployeeAccess>(db, request, visibility),
        });
    }
}
