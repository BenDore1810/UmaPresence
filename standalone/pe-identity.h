/* Hash every PE byte except its checksum and a well-formed, detached
   Authenticode certificate table at EOF. Keep sections, layout, timestamp,
   imports, exports, resources and any other overlay bytes in the identity. */
#ifndef UMAPRESENCE_PE_IDENTITY_H
#define UMAPRESENCE_PE_IDENTITY_H
#include <stddef.h>
#include <stdint.h>

typedef struct {
    size_t checksum;
    size_t certificate_directory;
    size_t content_end;
} PeIdentity;

static uint16_t pe_identity_u16(const unsigned char *data) {
    return (uint16_t)(data[0] | (uint16_t)data[1] << 8);
}

static uint32_t pe_identity_u32(const unsigned char *data) {
    return (uint32_t)data[0] | (uint32_t)data[1] << 8 |
        (uint32_t)data[2] << 16 | (uint32_t)data[3] << 24;
}

static int pe_identity_ranges(const unsigned char *data, size_t length, PeIdentity *identity) {
    if (length < 64 || length > 0x40000000u || pe_identity_u16(data) != 0x5a4d) return 0;
    size_t pe = pe_identity_u32(data + 60);
    if (pe < 64 || pe > length - 24 || pe_identity_u32(data + pe) != 0x4550 ||
        pe_identity_u16(data + pe + 4) != 0x8664) return 0;
    size_t optional = pe + 24, optional_size = pe_identity_u16(data + pe + 20);
    size_t count = pe_identity_u16(data + pe + 6);
    if (optional_size < 152 || optional_size > length - optional || !count || count > 96 ||
        pe_identity_u16(data + optional) != 0x20b || pe_identity_u32(data + optional + 108) < 5) return 0;
    size_t table = optional + optional_size;
    size_t mapped_end = pe_identity_u32(data + optional + 60);
    if (mapped_end < table || mapped_end > length || count > (mapped_end - table) / 40) return 0;
    for (size_t i = 0; i < count; i++) {
        const unsigned char *section = data + table + i * 40;
        size_t size = pe_identity_u32(section + 16), offset = pe_identity_u32(section + 20);
        if (!size) continue;
        if (offset < pe_identity_u32(data + optional + 60) || offset > length || size > length - offset) return 0;
        if (offset + size > mapped_end) mapped_end = offset + size;
    }
    size_t security = optional + 144;
    size_t certificate = pe_identity_u32(data + security), size = pe_identity_u32(data + security + 4);
    if (certificate || size) {
        if (certificate < mapped_end || certificate > length || (certificate & 7u) ||
            size < 8 || size != length - certificate) return 0;
        for (size_t cursor = certificate; cursor < length;) {
            size_t remaining = length - cursor;
            if (remaining < 8) return 0;
            size_t bytes = pe_identity_u32(data + cursor);
            if (bytes <= 8 || bytes > remaining || pe_identity_u16(data + cursor + 4) != 0x200 ||
                pe_identity_u16(data + cursor + 6) != 2) return 0;
            size_t aligned = (bytes + 7u) & ~(size_t)7u;
            if (aligned > remaining) return 0;
            for (size_t i = bytes; i < aligned; i++) if (data[cursor + i]) return 0;
            cursor += aligned;
        }
    }
    *identity = (PeIdentity){ optional + 64, security, certificate ? certificate : length };
    return 1;
}
#endif
