/* UmaPresence native host ABI v1. The Hachimi name lookup adapter is retained
   only as an internal compatibility shim for existing typed readers. */
typedef struct {
    u32 size, version;
    GetApi lookup;
    u64 capabilities;
} UmapresenceRuntimeServicesV1;
#define UMAPRESENCE_SERVICE_ABI 1
