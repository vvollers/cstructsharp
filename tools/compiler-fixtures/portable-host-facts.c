#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <wchar.h>

struct qa03_mixed {
    uint8_t tag;
    uint32_t count;
    uint16_t code;
};

struct qa03_item {
    uint8_t tag;
    uint32_t value;
};

struct qa03_nested {
    uint16_t prefix;
    struct qa03_item items[2];
    uint8_t tail;
};

union qa03_choice {
    uint8_t small;
    uint32_t large;
};

struct qa03_bits {
    unsigned char low : 3;
    unsigned char high : 5;
    uint16_t next;
};

struct qa03_pointer {
    uint8_t marker;
    uint16_t *target;
};

enum qa03_enum {
    QA03_ENUM_ZERO = 0,
    QA03_ENUM_HIGH = 0x7FFFFFFF
};

/*
 * Shapes where compiler families diverge or where a Portable rule is easy to get wrong. Each shape prints its size,
 * alignment, and byte image with the values below; contracts/quality/compiler-fixtures/shapes.json carries the same
 * values with the Portable declaration, and CompilerDifferentialFixtureTests compares the two byte for byte.
 */
struct shape_bits_u8_u16 { uint8_t a : 4; uint16_t b : 4; };
struct shape_bits_u8_u8_u16 { uint8_t a : 3; uint8_t b : 5; uint16_t c; };
struct shape_bits_u32_3_29_1 { uint32_t a : 3; uint32_t b : 29; uint32_t c : 1; };
struct shape_bits_u16_15_u8_2 { uint16_t a : 15; uint8_t b : 2; };
struct shape_bits_zero_width { uint8_t a : 3; uint8_t : 0; uint8_t b : 3; };
struct shape_bits_zero_width_u32 { uint8_t a : 3; uint32_t : 0; uint8_t b : 3; };
struct shape_bits_signed { int8_t a : 3; uint8_t b : 5; };
struct shape_bits_u8_6_6 { uint8_t a : 6; uint8_t b : 6; };
struct shape_bits_u64_u8 { uint64_t a : 4; uint8_t b : 4; };
struct shape_bits_after_byte { uint8_t x; uint32_t a : 4; uint8_t b : 4; };
struct shape_u64_after_u8 { uint8_t a; uint64_t b; };
struct shape_double_after_u8 { uint8_t a; double b; };
struct shape_long { uint8_t a; long b; };
enum shape_big_enum { SHAPE_BIG_ENUM_ZERO = 0, SHAPE_BIG_ENUM_BIG = 0x7FFFFFFF };
struct shape_enum_large { uint8_t a; enum shape_big_enum b; };
struct shape_bool { uint8_t a; _Bool b; uint16_t c; };
#pragma pack(push, 2)
struct shape_pack2_array { uint8_t a; uint32_t b[2]; uint8_t c; };
#pragma pack(pop)
struct shape_inner { uint8_t a; uint32_t b; };
struct shape_nested_align { uint8_t x; struct shape_inner in; uint8_t y; };
union shape_union_size { uint8_t a; uint32_t b; uint16_t c[3]; };
#pragma pack(push, 1)
struct shape_packed_bits_u8_u16 { uint8_t a : 4; uint16_t b : 4; };
struct shape_packed_bits_u8_6_6 { uint8_t a : 6; uint8_t b : 6; };
struct shape_packed_bits_u16_15_u8_2 { uint16_t a : 15; uint8_t b : 2; };
struct shape_packed_bits_after_byte { uint8_t x; uint32_t a : 4; uint8_t b : 4; };
#pragma pack(pop)

#define PRINT_SHAPE(id, T, value, last) \
    do { \
        T shape = value; \
        (void)printf("\"" id "\":{\"size\":%zu,\"alignment\":%zu,\"bytes\":\"", sizeof(T), _Alignof(T)); \
        print_bytes(&shape, sizeof shape); \
        (void)printf(last ? "\"}" : "\"},"); \
    } while (0)

static void print_bytes(const void *value, size_t size)
{
    const unsigned char *bytes = (const unsigned char *)value;
    size_t index;

    for (index = 0; index < size; ++index) {
        (void)printf("%02X", (unsigned int)bytes[index]);
    }
}

int main(void)
{
    const uint16_t endian_probe = UINT16_C(0x0102);
    const char *endian =
        (*(const unsigned char *)&endian_probe == UINT8_C(0x02)) ? "little" : "big";
    struct qa03_mixed mixed = { 0 };
    struct qa03_nested nested = { 0 };
    union qa03_choice choice = { 0 };
    struct qa03_bits bits = { 0 };

    mixed.tag = UINT8_C(0x11);
    mixed.count = UINT32_C(0x22334455);
    mixed.code = UINT16_C(0x6677);

    nested.prefix = UINT16_C(0x1234);
    nested.items[0].tag = UINT8_C(0xA1);
    nested.items[0].value = UINT32_C(0x11223344);
    nested.items[1].tag = UINT8_C(0xA2);
    nested.items[1].value = UINT32_C(0x55667788);
    nested.tail = UINT8_C(0xEE);

    choice.small = UINT8_C(0xA5);

    bits.low = 5U;
    bits.high = 17U;
    bits.next = UINT16_C(0x1234);

    (void)printf("{");
    (void)printf("\"endian\":\"%s\",", endian);
    (void)printf(
        "\"char\":{\"size\":%zu,\"alignment\":%zu,\"signed\":%s},",
        sizeof(char),
        _Alignof(char),
        ((char)-1 < 0) ? "true" : "false");
    (void)printf(
        "\"short\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(short),
        _Alignof(short));
    (void)printf(
        "\"int\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(int),
        _Alignof(int));
    (void)printf(
        "\"long\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(long),
        _Alignof(long));
    (void)printf(
        "\"longLong\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(long long),
        _Alignof(long long));
    (void)printf(
        "\"wchar\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(wchar_t),
        _Alignof(wchar_t));
    (void)printf(
        "\"pointer\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(void *),
        _Alignof(void *));
    (void)printf(
        "\"enum\":{\"size\":%zu,\"alignment\":%zu},",
        sizeof(enum qa03_enum),
        _Alignof(enum qa03_enum));

    (void)printf(
        "\"fixedWidthAggregate\":{\"size\":%zu,\"alignment\":%zu,"
        "\"offsets\":{\"tag\":%zu,\"count\":%zu,\"code\":%zu},\"bytes\":\"",
        sizeof(struct qa03_mixed),
        _Alignof(struct qa03_mixed),
        offsetof(struct qa03_mixed, tag),
        offsetof(struct qa03_mixed, count),
        offsetof(struct qa03_mixed, code));
    print_bytes(&mixed, sizeof(mixed));
    (void)printf("\"},");

    (void)printf(
        "\"nestedArray\":{\"size\":%zu,\"alignment\":%zu,"
        "\"offsets\":{\"prefix\":%zu,\"items0Tag\":%zu,\"items0Value\":%zu,"
        "\"items1Tag\":%zu,\"items1Value\":%zu,\"tail\":%zu},\"bytes\":\"",
        sizeof(struct qa03_nested),
        _Alignof(struct qa03_nested),
        offsetof(struct qa03_nested, prefix),
        offsetof(struct qa03_nested, items[0].tag),
        offsetof(struct qa03_nested, items[0].value),
        offsetof(struct qa03_nested, items[1].tag),
        offsetof(struct qa03_nested, items[1].value),
        offsetof(struct qa03_nested, tail));
    print_bytes(&nested, sizeof(nested));
    (void)printf("\"},");

    (void)printf(
        "\"union\":{\"size\":%zu,\"alignment\":%zu,"
        "\"offsets\":{\"small\":%zu,\"large\":%zu},\"bytes\":\"",
        sizeof(union qa03_choice),
        _Alignof(union qa03_choice),
        offsetof(union qa03_choice, small),
        offsetof(union qa03_choice, large));
    print_bytes(&choice, sizeof(choice));
    (void)printf("\"},");

    (void)printf(
        "\"bitfield\":{\"size\":%zu,\"alignment\":%zu,"
        "\"offsets\":{\"next\":%zu},\"bytes\":\"",
        sizeof(struct qa03_bits),
        _Alignof(struct qa03_bits),
        offsetof(struct qa03_bits, next));
    print_bytes(&bits, sizeof(bits));
    (void)printf("\"},");

    (void)printf(
        "\"pointerAggregate\":{\"size\":%zu,\"alignment\":%zu,"
        "\"offsets\":{\"marker\":%zu,\"target\":%zu}}",
        sizeof(struct qa03_pointer),
        _Alignof(struct qa03_pointer),
        offsetof(struct qa03_pointer, marker),
        offsetof(struct qa03_pointer, target));
    (void)printf(",\"shapes\":{");
    PRINT_SHAPE("bits-u8-u16", struct shape_bits_u8_u16, ((struct shape_bits_u8_u16){ .a = 0xF, .b = 0xA }), 0);
    PRINT_SHAPE("bits-u8-u8-u16", struct shape_bits_u8_u8_u16, ((struct shape_bits_u8_u8_u16){ .a = 7, .b = 0x1F, .c = 0xABCD }), 0);
    PRINT_SHAPE("bits-u32-3-29-1", struct shape_bits_u32_3_29_1, ((struct shape_bits_u32_3_29_1){ .a = 7, .b = 0x1FFFFFFF, .c = 1 }), 0);
    PRINT_SHAPE("bits-u16-15-u8-2", struct shape_bits_u16_15_u8_2, ((struct shape_bits_u16_15_u8_2){ .a = 0x7FFF, .b = 3 }), 0);
    PRINT_SHAPE("bits-zero-width", struct shape_bits_zero_width, ((struct shape_bits_zero_width){ .a = 7, .b = 7 }), 0);
    PRINT_SHAPE("bits-zero-width-u32", struct shape_bits_zero_width_u32, ((struct shape_bits_zero_width_u32){ .a = 7, .b = 7 }), 0);
    PRINT_SHAPE("bits-signed", struct shape_bits_signed, ((struct shape_bits_signed){ .a = -1, .b = 0x1F }), 0);
    PRINT_SHAPE("bits-u8-6-6", struct shape_bits_u8_6_6, ((struct shape_bits_u8_6_6){ .a = 0x3F, .b = 0x3F }), 0);
    PRINT_SHAPE("bits-u64-u8", struct shape_bits_u64_u8, ((struct shape_bits_u64_u8){ .a = 0xF, .b = 0xF }), 0);
    PRINT_SHAPE("bits-after-byte", struct shape_bits_after_byte, ((struct shape_bits_after_byte){ .x = 0xAA, .a = 0xF, .b = 0xF }), 0);
    PRINT_SHAPE("u64-after-u8", struct shape_u64_after_u8, ((struct shape_u64_after_u8){ .a = 0x11, .b = UINT64_C(0x8877665544332211) }), 0);
    PRINT_SHAPE("double-after-u8", struct shape_double_after_u8, ((struct shape_double_after_u8){ .a = 0x11, .b = 1.5 }), 0);
    PRINT_SHAPE("long", struct shape_long, ((struct shape_long){ .a = 0x11, .b = 0x12345678L }), 0);
    PRINT_SHAPE("enum-large", struct shape_enum_large, ((struct shape_enum_large){ .a = 0x11, .b = SHAPE_BIG_ENUM_BIG }), 0);
    PRINT_SHAPE("bool", struct shape_bool, ((struct shape_bool){ .a = 0x11, .b = 1, .c = 0x2233 }), 0);
    PRINT_SHAPE("pack2-array", struct shape_pack2_array, ((struct shape_pack2_array){ .a = 0x11, .b = { 0x22334455, 0x66778899 }, .c = 0xAA }), 0);
    PRINT_SHAPE("nested-align", struct shape_nested_align, ((struct shape_nested_align){ .x = 0x11, .in = { .a = 0x22, .b = 0x33445566 }, .y = 0x77 }), 0);
    PRINT_SHAPE("union-size", union shape_union_size, ((union shape_union_size){ .c = { 0x1122, 0x3344, 0x5566 } }), 0);
    PRINT_SHAPE("packed-bits-u8-u16", struct shape_packed_bits_u8_u16, ((struct shape_packed_bits_u8_u16){ .a = 0xF, .b = 0xA }), 0);
    PRINT_SHAPE("packed-bits-u8-6-6", struct shape_packed_bits_u8_6_6, ((struct shape_packed_bits_u8_6_6){ .a = 0x3F, .b = 0x3F }), 0);
    PRINT_SHAPE("packed-bits-u16-15-u8-2", struct shape_packed_bits_u16_15_u8_2, ((struct shape_packed_bits_u16_15_u8_2){ .a = 0x7FFF, .b = 3 }), 0);
    PRINT_SHAPE("packed-bits-after-byte", struct shape_packed_bits_after_byte, ((struct shape_packed_bits_after_byte){ .x = 0xAA, .a = 0xF, .b = 0xF }), 1);
    (void)printf("}");
    (void)printf("}\n");

    return 0;
}
