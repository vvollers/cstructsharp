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
    (void)printf("}\n");

    return 0;
}
