import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';

// Shape copied from a real response of GET /api/catalog/products (2026-10-07).
const _item = {
  'id': '20000000-0000-0000-0000-000000000005',
  'name': 'Daily Rice',
  'description': 'Long grain rice for every meal',
  'price': 89.00,
  'unitOfMeasure': '1 kg',
  'categoryId': '10000000-0000-0000-0000-000000000001',
  'imageUrl': 'https://images.example/rice.jpg',
};

void main() {
  test('CatalogProductDto parses a real API item', () {
    final p = CatalogProductDto.fromJson(_item);
    expect((p.name, p.price, p.unitOfMeasure, p.imageUrl), ('Daily Rice', 89.0, '1 kg', 'https://images.example/rice.jpg'));
  });

  test('MRP, discount percent and translations are parsed; locale keys are lowercased', () {
    final p = CatalogProductDto.fromJson({
      ..._item,
      'mrp': 99,
      'discountPercent': 10,
      'translations': {'MR': {'name': 'तांदूळ', 'description': null}}
    });
    expect((p.mrp, p.discountPercent), (99.0, 10));
    expect(p.translations['mr']?.name, 'तांदूळ');
    expect(p.translations['mr']?.description, isNull);
  });

  test('an older server without mrp or translations still parses (mrp = price, no translations)', () {
    final p = CatalogProductDto.fromJson(_item);
    expect((p.mrp, p.discountPercent), (89.0, 0));
    expect(p.translations, isEmpty);
  });

  test('CatalogCategoryDto parses translations and rejects a malformed one', () {
    final c = CatalogCategoryDto.fromJson({'id': 'c', 'name': 'Dairy', 'parentCategoryId': null, 'translations': {'mr': {'name': 'दुग्धजन्य'}}});
    expect((c.name, c.translations['mr']?.name), ('Dairy', 'दुग्धजन्य'));
    expect(() => CatalogCategoryDto.fromJson({'id': 'c', 'name': 'Dairy', 'translations': {'mr': {}}}), throwsFormatException);
  });

  test('imageUrl may be null and description may be missing', () {
    final p = CatalogProductDto.fromJson({..._item, 'imageUrl': null, 'description': null});
    expect((p.imageUrl, p.description), (null, ''));
  });

  test('PagedDto parses items and paging metadata', () {
    final page = PagedDto.fromJson({'items': [_item, _item], 'page': 2, 'pageSize': 2, 'totalCount': 5, 'hasMore': true}, CatalogProductDto.fromJson);
    expect((page.items.length, page.page, page.pageSize, page.totalCount, page.hasMore), (2, 2, 2, 5, true));
  });

  test('PagedDto rejects a payload without paging fields or with a bad item', () {
    expect(() => PagedDto.fromJson({'items': []}, CatalogProductDto.fromJson), throwsFormatException);
    expect(() => PagedDto.fromJson({'items': [{'id': 1}], 'page': 1, 'pageSize': 1, 'totalCount': 1, 'hasMore': false}, CatalogProductDto.fromJson), throwsFormatException);
    expect(() => PagedDto.fromJson('nope', CatalogProductDto.fromJson), throwsFormatException);
  });
}
