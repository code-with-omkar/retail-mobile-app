import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';

import '../support/fake_adapter.dart';

const _veg = '10000000-0000-0000-0000-000000000002';

Map<String, dynamic> _item(String id, String? imageUrl) => {'id': id, 'name': 'P $id', 'description': 'd', 'price': 10, 'mrp': 10, 'discountPercent': 0, 'unitOfMeasure': '1 kg', 'categoryId': _veg, 'imageUrl': imageUrl};

void main() {
  late FakeAdapter adapter;
  late ApiCatalogRepository repo;
  setUp(() {
    adapter = FakeAdapter();
    repo = ApiCatalogRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test:5067'), dio: Dio()..httpClientAdapter = adapter));
  });

  group('pictures the API serves itself', () {
    test('a path from the server is turned into a full address on the same server; a full link is left alone', () async {
      adapter
        ..replyJson({'success': true, 'data': [{'id': _veg, 'name': 'Vegetables', 'parentCategoryId': null, 'isActive': true}]})
        ..replyJson({
          'success': true,
          'data': {
            'items': [_item('a', '/media/products/a-1234abcd.jpg'), _item('b', 'https://images.example.com/b.jpg'), _item('c', null)],
            'page': 1,
            'pageSize': 20,
            'totalCount': 3,
            'hasMore': false
          }
        });

      final page = await repo.products();

      expect(page.items.map((p) => p.imageUrl), ['http://api.test:5067/media/products/a-1234abcd.jpg', 'https://images.example.com/b.jpg', null]);
    });

    test('the helper only changes paths that start with a slash', () {
      expect(absoluteImageUrl('/media/x.png', 'http://h'), 'http://h/media/x.png');
      expect(absoluteImageUrl('http://other/x.png', 'http://h'), 'http://other/x.png');
      expect(absoluteImageUrl('/media/x.png', null), '/media/x.png');
      expect(absoluteImageUrl(null, 'http://h'), isNull);
    });
  });
}
