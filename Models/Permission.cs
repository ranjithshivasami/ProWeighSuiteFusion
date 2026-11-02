namespace ProWeighSuitePlatforms.Models
{
    public enum Permission
    {
        // Dashboard/Weighing Operations
        ViewDashboard,
        StartWeighing,
        AcceptWeight,
        PauseBatch,
        AbortBatch,
        
        // Materials Management
        ViewMaterials,
        CreateMaterial,
        EditMaterial,
        DeleteMaterial,
        ImportMaterial,
        DownloadMaterialTemplate,
        ExportMaterial,
        
        // Recipe Management
        ViewRecipes,
        CreateRecipe,
        EditRecipe,
        DeleteRecipe,
        PrintRecipe,
        
        // Category Management
        ViewCategory,
        CreateCategory,
        EditCategory,
        DeleteCategory,
        ImportCategory,
        DownloadCategoryTemplate,
        ExportCategory,
        
        // Variant Management
        ViewVariant,
        CreateVariant,
        EditVariant,
        DeleteVariant,
        ImportVariant,
        DownloadVariantTemplate,
        ExportVariant,

        // Supplier Management
        ViewSupplier,
        CreateSupplier,
        EditSupplier,
        DeleteSupplier,
        ImportSupplier,
        DownloadSupplierTemplate,
        ExportSupplier,
        
        // Scale Settings
        ViewScaleSettings,
        CreateScaleSettings,
        EditScaleSettings,
        DeleteScaleSettings,
        
        // User Management
        ViewUsers,
        CreateUser,
        EditUser,
        DeleteUser,
        ChangeUserRole,
        
        // Reports
        ViewReports,
        ExportReports,
        
        // System Settings
        ViewSystemSettings,
        EditSystemSettings,
        
        // Role Permissions Management
        ViewRolePermissions,
        EditRolePermissions,
        
        // Company Settings
        ViewCompanySettings,
        EditCompanySettings,

        // Module Management (Phase 6: DB-backed module assignments + single-active weighing)
        ViewModules,
        EditModules,

        // Configuration submenu access (one permission per menu so each can be
        // granted/revoked independently in the role permissions matrix).
        ViewAppSettings,
        ViewSyncSettings,
        ViewBackupSettings,
        ViewBackupLog,
        ViewReportSchedule,
        ViewLabelTemplates,
        ViewPrinterSetup,
        ViewPlcSettings,
        ViewScannerSettings,
        ViewCleanUpDatabase,

        // Fusion submenu access
        ViewFusionSettings,
        ViewBayStatus,
        ViewSupplierMaster,
        ViewTubMaster,
        ViewInwardStock,
        ViewBatchLots,
        ViewBatchStatus,
        ViewLiveView,
        ViewFusionReports
    }
}
