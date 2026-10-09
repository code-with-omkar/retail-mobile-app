import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/config/app_config.dart';
import 'package:quickcart_customer/data/api/api_client.dart';
import 'package:quickcart_customer/data/api/api_exception.dart';
import 'package:quickcart_customer/data/dto/address_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/repositories/address_repository.dart';

import '../support/fake_adapter.dart';

Map<String, dynamic> _address({String id = 'a1', String label = 'Home', bool isDefault = true, bool? serviceable = true, String reason = 'Serviceable', String flat = 'Flat 4', String landmark = 'Near the station'}) => {
      'id': id,
      'label': label,
      'line': '12 Marine Drive, Mumbai',
      'flatOrBuilding': flat,
      'landmark': landmark,
      'latitude': 19.076,
      'longitude': 72.8777,
      'receiverName': 'Asha Patil',
      'receiverPhone': '9876543210',
      'isDefault': isDefault,
      'serviceable': ?serviceable,
      'serviceabilityReason': reason,
    };

const _draft = AddressDraft(label: '  Home ', line: ' 12 Marine Drive, Mumbai ', flatOrBuilding: ' Flat 4 ', landmark: '', latitude: 19.076, longitude: 72.8777, receiverName: ' Asha Patil ', receiverPhone: '98765 43210');

void main() {
  late FakeAdapter adapter;
  late ApiAddressRepository repo;
  setUp(() {
    adapter = FakeAdapter();
    repo = ApiAddressRepository(ApiClient(config: const AppConfig(env: AppEnv.dev, apiBaseUrl: 'http://api.test'), dio: Dio()..httpClientAdapter = adapter));
  });

  group('reading', () {
    test('the list parses every field, in the order the server sends', () async {
      adapter.replyJson({
        'success': true,
        'data': [_address(), _address(id: 'a2', label: 'Delhi', isDefault: false, serviceable: false, reason: 'OutsideServiceArea', flat: '')],
      });

      final list = await repo.list();

      expect(adapter.requests.single.path, '/api/customer/addresses');
      expect(list.map((a) => a.id), ['a1', 'a2']);
      final home = list.first;
      expect((home.label, home.line, home.flatOrBuilding, home.landmark, home.latitude, home.longitude), ('Home', '12 Marine Drive, Mumbai', 'Flat 4', 'Near the station', 19.076, 72.8777));
      expect((home.receiverName, home.receiverPhone, home.isDefault, home.serviceable), ('Asha Patil', '9876543210', true, true));
      expect(home.fullLine, 'Flat 4, 12 Marine Drive, Mumbai');
      expect((list[1].serviceable, list[1].serviceabilityReason, list[1].fullLine), (false, ServiceabilityReasons.outsideServiceArea, '12 Marine Drive, Mumbai'));
    });

    test('an API that does not say whether an address is serviceable is treated as serviceable, never wrongly blocked', () async {
      adapter.replyJson({'success': true, 'data': [_address(serviceable: null)]});

      final home = (await repo.list()).single;

      expect((home.serviceable, home.serviceabilityReason), (true, ServiceabilityReasons.serviceable));
    });

    test('an empty list is fine', () async {
      adapter.replyJson({'success': true, 'data': []});
      expect(await repo.list(), isEmpty);
    });

    test('a field of the wrong type is reported as an unexpected response, not half-read', () async {
      adapter.replyJson({'success': true, 'data': [{..._address(), 'latitude': 'north'}]});
      await expectLater(repo.list(), throwsA(isA<UnexpectedResponseException>()));
    });
  });

  group('writing', () {
    test('create sends trimmed text and the point, and returns the saved address', () async {
      adapter.replyJson({'success': true, 'data': _address()}, status: 201);

      final saved = await repo.create(_draft);

      final request = adapter.requests.single;
      expect((request.method, request.path), ('POST', '/api/customer/addresses'));
      expect(request.data, {
        'label': 'Home',
        'line': '12 Marine Drive, Mumbai',
        'flatOrBuilding': 'Flat 4',
        'landmark': '',
        'latitude': 19.076,
        'longitude': 72.8777,
        'receiverName': 'Asha Patil',
        'receiverPhone': '98765 43210',
      });
      expect(saved.id, 'a1');
    });

    test('update puts to the address id', () async {
      adapter.replyJson({'success': true, 'data': _address(label: 'Parents')});

      final saved = await repo.update('a1', _draft);

      expect((adapter.requests.single.method, adapter.requests.single.path), ('PUT', '/api/customer/addresses/a1'));
      expect(saved.label, 'Parents');
    });

    test('make default posts to the default route', () async {
      adapter.replyJson({'success': true, 'data': _address(id: 'a2', isDefault: true)});

      final saved = await repo.setDefault('a2');

      expect((adapter.requests.single.method, adapter.requests.single.path), ('POST', '/api/customer/addresses/a2/default'));
      expect(saved.isDefault, isTrue);
    });

    test('delete calls delete on the address id and accepts an empty answer', () async {
      adapter.replyJson({'success': true, 'data': null});

      await repo.delete('a1');

      expect((adapter.requests.single.method, adapter.requests.single.path), ('DELETE', '/api/customer/addresses/a1'));
    });
  });

  group('failures keep their meaning', () {
    test('a validation answer carries the messages the server wrote for the customer', () async {
      adapter.replyJson({
        'success': false,
        'message': 'Pick the location on the map. Enter a phone number with 10 to 15 digits.',
        'errors': ['Pick the location on the map.', 'Enter a phone number with 10 to 15 digits.']
      }, status: 400);

      await expectLater(
        repo.create(_draft),
        throwsA(isA<ValidationException>().having((e) => e.errors, 'errors', ['Pick the location on the map.', 'Enter a phone number with 10 to 15 digits.'])),
      );
    });

    test('the address limit is a conflict', () async {
      adapter.replyJson({'success': false, 'message': 'You can save up to 10 addresses. Delete one to add another.', 'errors': []}, status: 409);
      await expectLater(repo.create(_draft), throwsA(isA<ConflictException>()));
    });

    test('someone elses address, or one that is gone, is not found', () async {
      adapter.replyJson({'success': false, 'message': 'Address not found.', 'errors': []}, status: 404);
      await expectLater(repo.delete('gone'), throwsA(isA<NotFoundException>()));
    });

    test('without a signed-in customer the server says 401', () async {
      adapter.replyJson({'success': false, 'message': 'Unauthorized'}, status: 401);
      await expectLater(repo.list(), throwsA(isA<UnauthorizedException>()));
    });
  });

  group('serviceability', () {
    test('asks for a point and reads the answer', () async {
      adapter.replyJson({'success': true, 'data': {'serviceable': false, 'reason': 'OutsideServiceArea', 'nearestDistanceKm': 1143.6}});

      final answer = await repo.serviceability(latitude: 28.61, longitude: 77.21);

      expect(adapter.requests.single.path, '/api/catalog/serviceability');
      expect(adapter.requests.single.queryParameters, {'latitude': 28.61, 'longitude': 77.21});
      expect((answer.serviceable, answer.reason, answer.nearestDistanceKm), (false, ServiceabilityReasons.outsideServiceArea, 1143.6));
    });

    test('a serviceable point without a distance is fine', () async {
      adapter.replyJson({'success': true, 'data': {'serviceable': true, 'reason': 'Serviceable'}});

      final answer = await repo.serviceability(latitude: 19.07, longitude: 72.87);

      expect((answer.serviceable, answer.nearestDistanceKm), (true, null));
    });

    test('impossible coordinates are a validation error', () async {
      adapter.replyJson({'success': false, 'message': 'Latitude must be between -90 and 90.', 'errors': ['Latitude must be between -90 and 90.']}, status: 400);
      await expectLater(repo.serviceability(latitude: 200, longitude: 0), throwsA(isA<ValidationException>()));
    });
  });

  group('delivery place model', () {
    test('the area is the part of the address before the city', () {
      DeliveryPlace place(String line) => DeliveryPlace(label: 'Home', line: line, latitude: 1, longitude: 1);
      expect(place('Flat 402, Sea Breeze Apts, Bandra West, Mumbai 400050').area, 'Bandra West');
      expect(place('12 Marine Drive, Mumbai').area, '12 Marine Drive');
      expect(place('Mumbai').area, 'Mumbai');
      expect(place('').area, '');
    });

    test('a place survives being written to and read from preferences, and a damaged one is ignored', () {
      const place = DeliveryPlace(label: 'Current location', line: '12 Marine Drive', latitude: 19.076, longitude: 72.8777);

      final back = DeliveryPlace.fromJson(place.toJson())!;

      expect((back.label, back.line, back.latitude, back.longitude, back.isSaved), ('Current location', '12 Marine Drive', 19.076, 72.8777, false));
      expect(DeliveryPlace.fromJson({'label': 'x'}), isNull);
      expect(DeliveryPlace.fromJson('nonsense'), isNull);
      expect(DeliveryPlace.fromJson(null), isNull);
    });

    test('the draft body never contains an id or the default flag, which only the server decides', () {
      final body = addressDraftToJson(_draft);
      expect(body.keys, isNot(contains('id')));
      expect(body.keys, isNot(contains('isDefault')));
      expect(body.keys, isNot(contains('customerId')));
    });
  });
}
