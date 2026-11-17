#include "../pe-identity.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

static void put32(unsigned char *image, size_t offset, uint32_t value) {
    for (size_t i = 0; i < 4; i++) image[offset + i] = (unsigned char)(value >> (8 * i));
}

int main(void) {
    unsigned char image[0x410] = {0};
    PeIdentity id;
    put32(image, 0, 0x5a4d);
    put32(image, 60, 64);
    put32(image, 64, 0x4550);
    put32(image, 68, 0x18664);
    put32(image, 84, 0xf0);
    put32(image, 88, 0x20b);
    put32(image, 88 + 60, 0x200);
    put32(image, 88 + 108, 16);
    put32(image, 88 + 0xf0 + 16, 0x200);
    put32(image, 88 + 0xf0 + 20, 0x200);
    assert(pe_identity_ranges(image, 0x400, &id));
    assert(id.checksum == 152 && id.certificate_directory == 232 && id.content_end == 0x400);
    put32(image, 232, 0x400);
    put32(image, 236, 16);
    put32(image, 0x400, 16);
    put32(image, 0x404, 0x20200);
    assert(pe_identity_ranges(image, sizeof(image), &id) && id.content_end == 0x400);
    puts("PASS: PE identity excludes only checksum and detached certificate data");
    put32(image, 0x400, 7);
    assert(!pe_identity_ranges(image, sizeof(image), &id));
    put32(image, 232, 0x3e0);
    put32(image, 236, 48);
    assert(!pe_identity_ranges(image, sizeof(image), &id));
    puts("PASS: malformed and section-overlapping certificates are rejected");
    return 0;
}
