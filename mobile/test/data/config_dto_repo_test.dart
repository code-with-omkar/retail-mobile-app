import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/core/strings_mr.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/data/repositories/health_repository.dart';

import '../support/fake_adapter.dart';

void main() {
  group('AppConfig.resolve', () {
    test('dev defaults to the emulator host on Android and localhost elsewhere', () {
      expect(AppConfig.resolve(envName: 'dev', baseUrl: '', androidDevice: true).apiBaseUrl, 'http://10.0.2.2:5067');
      expect(AppConfig.resolve(envName: 'dev', baseUrl: '', androidDevice: false).apiBaseUrl, 'http://localhost:5067');
    });

    test('an explicit URL wins and a trailing slash is trimmed', () {
      expect(AppConfig.resolve(envName: 'dev', baseUrl: 'http://192.168.1.20:5067/', androidDevice: true).apiBaseUrl, 'http://192.168.1.20:5067');
    });

    test('staging and prod require a URL; prod requires https', () {
      expect(() => AppConfig.resolve(envName: 'staging', baseUrl: '', androidDevice: true), throwsStateError);
      expect(() => AppConfig.resolve(envName: 'prod', baseUrl: 'http://api.example.com', androidDevice: true), throwsStateError);
      expect(AppConfig.resolve(envName: 'prod', baseUrl: 'https://api.example.com', androidDevice: true).isProd, isTrue);
    });

    test('rejects an unknown environment and a malformed URL', () {
      expect(() => AppConfig.resolve(envName: 'qa', baseUrl: '', androidDevice: true), throwsArgumentError);
      expect(() => AppConfig.resolve(envName: 'dev', baseUrl: 'not a url', androidDevice: true), throwsArgumentError);
    });
  });

  group('catalog DTOs', () {
    test('CategoryDto parses the API shape', () {
      final c = CategoryDto.fromJson({'id': 'c1', 'name': 'Dairy', 'parentCategoryId': null, 'isActive': true});
      expect((c.id, c.name, c.parentCategoryId, c.isActive), ('c1', 'Dairy', null, true));
    });

    test('ProductDto accepts integer or decimal prices and a null image', () {
      final json = {'id': 'p1', 'sku': 'S1', 'name': 'Milk', 'description': 'd', 'price': 34, 'unitOfMeasure': '500 ml', 'categoryId': 'c1', 'imageUrl': null, 'isActive': true};
      expect(ProductDto.fromJson(json).price, 34.0);
      expect(ProductDto.fromJson({...json, 'price': 34.5}).price, 34.5);
      expect(ProductDto.fromJson(json).imageUrl, isNull);
    });

    test('a missing or mistyped required field throws FormatException', () {
      expect(() => CategoryDto.fromJson({'id': 'c1', 'isActive': true}), throwsFormatException);
      expect(() => ProductDto.fromJson({'id': 'p1', 'sku': 'S', 'name': 'n', 'price': 'free', 'unitOfMeasure': 'kg', 'categoryId': 'c', 'isActive': true}), throwsFormatException);
    });

    test('StoreDto parses coordinates and radius', () {
      final s = StoreDto.fromJson({'id': 's1', 'name': 'Store', 'address': 'A', 'latitude': 19.05, 'longitude': 72.83, 'serviceRadiusKm': 5, 'isActive': true});
      expect((s.latitude, s.serviceRadiusKm), (19.05, 5.0));
    });
  });

  group('repositories', () {
    late FakeAdapter adapter;
    late ApiClient client;
    setUp(() {
      adapter = FakeAdapter();
      client = ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter);
    });

    test('ApiCatalogRepository maps the categories the server returns (active only) and picks icons', () async {
      adapter.replyJson({
        'success': true,
        'data': [
          {'id': 'a', 'name': 'Vegetables', 'parentCategoryId': null, 'isActive': true},
          {'id': 'b', 'name': 'Hardware', 'parentCategoryId': null, 'isActive': true},
        ]
      });
      final cats = await ApiCatalogRepository(client).categories();
      expect(cats.map((c) => c.label), ['Vegetables', 'Hardware']);
      expect(cats.first.icon, Icons.eco_outlined);
      expect(cats.last.icon, Icons.category_outlined);
      expect(adapter.requests.single.path, '/api/catalog/categories');
    });

    test('SeedCatalogRepository returns the local categories', () async {
      expect((await const SeedCatalogRepository().categories()).isNotEmpty, isTrue);
    });

    test('HealthRepository reports live and ready separately', () async {
      adapter
        ..replyText('Healthy')
        ..replyText('Unhealthy', status: 503);
      final report = await HealthRepository(client).check();
      expect((report.liveOk, report.readyOk, report.readyBody), (true, false, 'Unhealthy'));
    });
  });

  test('every API error message has a Marathi translation', () {
    expect(apiErrorMessageKeys.where((k) => !marathi.containsKey(k)), isEmpty);
  });
}
