import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:quickcart_customer/core/l10n.dart';
import 'package:quickcart_customer/data/repositories/offers_repository.dart';
import 'package:quickcart_customer/features/offers/festival_card.dart';
import 'package:quickcart_customer/features/offers/offers_controller.dart';

Offer _offer({String? design = 'diwali', String title = 'Up to 20% off sweets', String? titleMr = 'मिठाईवर २०% सूट', String? image, DateTime? endsAt}) =>
    Offer(id: 'o1', title: title, titleMr: titleMr, cardDesign: design, imageUrl: image, endsAt: endsAt, discountPercent: 20);

Future<void> _pump(WidgetTester tester, Offer offer, {Locale locale = const Locale('en'), double width = 350, double textScale = 1}) async {
  await tester.pumpWidget(MaterialApp(
    locale: locale,
    supportedLocales: AppStrings.supported,
    localizationsDelegates: const [AppStrings.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
    home: MediaQuery(
      data: MediaQueryData(textScaler: TextScaler.linear(textScale)),
      child: Scaffold(body: Center(child: SizedBox(width: width, child: FestivalCard(offer)))),
    ),
  ));
  await tester.pump();
}

/// The big name line of the card: the festival in English, with its Marathi name after it.
Finder _nameLine(String name, String mr) => find.byWidgetPredicate((w) => w is RichText && w.text.toPlainText().startsWith(name) && w.text.toPlainText().contains(mr));

void main() {
  group('the eight festival designs', () {
    test('are the eight the API stores, in the same order', () {
      expect(festivalThemes.map((t) => t.key), ['diwali', 'ganesh', 'navratri', 'holi', 'sankranti', 'padwa', 'eid', 'christmas']);
    });

    test('each has an English and a Marathi name and a button text, and an unknown key has no theme', () {
      for (final t in festivalThemes) {
        expect(t.name, isNotEmpty);
        expect(RegExp(r'[ऀ-ॿ]').hasMatch(t.mr), isTrue, reason: t.key);
        expect(t.cta, isNotEmpty);
      }
      expect(festivalThemeOf('holi')?.name, 'Holi');
      expect(festivalThemeOf('onam'), isNull);
      expect(festivalThemeOf(null), isNull);
    });

    test('name, line and button text each pass WCAG AA (4.5:1) on their background', () {
      double lum(Color c) {
        double lin(double v) => v <= 0.03928 ? v / 12.92 : math.pow((v + 0.055) / 1.055, 2.4).toDouble();
        return 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b);
      }

      double ratio(Color a, Color b) {
        final hi = lum(a) > lum(b) ? lum(a) : lum(b);
        final lo = lum(a) > lum(b) ? lum(b) : lum(a);
        return (hi + .05) / (lo + .05);
      }

      for (final t in festivalThemes) {
        expect(ratio(t.ink, t.bg), greaterThanOrEqualTo(4.5), reason: '${t.key} name');
        expect(ratio(t.muted, t.bg), greaterThanOrEqualTo(4.5), reason: '${t.key} line');
        expect(ratio(t.ctaInk, t.ctaBg), greaterThanOrEqualTo(4.5), reason: '${t.key} button');
      }
    });
  });

  group('the card', () {
    for (final t in festivalThemes) {
      testWidgets('${t.key}: shows its names, the offer line, the chip and the button, and draws its motif without trouble', (tester) async {
        await _pump(tester, _offer(design: t.key));

        expect(_nameLine(t.name, t.mr), findsOneWidget); // the festival name, then its Marathi name beside it
        expect(find.text('Up to 20% off sweets'), findsOneWidget);
        expect(find.text('FESTIVE OFFER'), findsOneWidget);
        expect(find.text(t.cta), findsOneWidget);
        expect(find.byWidgetPredicate((w) => w is CustomPaint && w.painter is FestivalMotifPainter), findsOneWidget);
        expect(tester.takeException(), isNull);
      });
    }

    testWidgets('each motif is a different drawing', (tester) async {
      final drawings = <String>{};
      for (final t in festivalThemes) {
        await _pump(tester, _offer(design: t.key));
        final painter = tester.widget<CustomPaint>(find.byWidgetPredicate((w) => w is CustomPaint && w.painter is FestivalMotifPainter)).painter! as FestivalMotifPainter;
        drawings.add(painter.theme.key);
      }
      expect(drawings.length, 8);
    });

    testWidgets('the chip counts the days left and says when it ends today', (tester) async {
      await _pump(tester, _offer(endsAt: DateTime.now().add(const Duration(days: 3) - const Duration(hours: 1))));
      expect(find.text('FESTIVE OFFER · ENDS IN 3 DAYS'), findsOneWidget);

      await _pump(tester, _offer(endsAt: DateTime.now().add(const Duration(hours: 20))));
      expect(find.text('FESTIVE OFFER · ENDS IN 1 DAY'), findsOneWidget);

      await _pump(tester, _offer(endsAt: DateTime.now().subtract(const Duration(hours: 1))));
      expect(find.text('FESTIVE OFFER · ENDS TODAY'), findsOneWidget);
    });

    testWidgets('in Marathi the chip, the button and the offer line are Marathi, and both names stay', (tester) async {
      await _pump(tester, _offer(design: 'ganesh', endsAt: DateTime.now().add(const Duration(days: 3) - const Duration(hours: 1))), locale: const Locale('mr'));

      expect(find.text('मिठाईवर २०% सूट'), findsOneWidget);
      expect(find.textContaining('सणाची ऑफर', findRichText: true), findsOneWidget);
      expect(find.text('पूजा खरेदी'), findsOneWidget);
      expect(_nameLine('Ganeshotsav', 'गणेशोत्सव'), findsOneWidget);
    });

    testWidgets('without a Marathi title the Marathi reader gets the English one', (tester) async {
      await _pump(tester, _offer(titleMr: null), locale: const Locale('mr'));
      expect(find.text('Up to 20% off sweets'), findsOneWidget);
    });

    testWidgets('is one control for a screen reader, labelled with the offer title', (tester) async {
      final handle = tester.ensureSemantics();
      await _pump(tester, _offer());

      expect(find.bySemanticsLabel('Up to 20% off sweets'), findsOneWidget);
      expect(find.bySemanticsLabel('FESTIVE OFFER'), findsNothing); // the parts are not separate stops
      handle.dispose();
    });

    testWidgets('never overflows, even narrow, with large text and a long title in both scripts', (tester) async {
      final long = _offer(design: 'sankranti', title: 'Til-gul, jaggery and kite-day snacks at a really long festival price', titleMr: 'तीळगूळ, गूळ आणि पतंगाच्या दिवसाचा खाऊ अतिशय कमी किमतीत मिळेल');
      for (final width in const [350.0, 300.0, 260.0]) {
        for (final scale in const [1.0, 1.5]) {
          await _pump(tester, long, width: width, textScale: scale);
          expect(tester.takeException(), isNull, reason: 'English at $width wide, text x$scale');
          await _pump(tester, long, locale: const Locale('mr'), width: width, textScale: scale);
          expect(tester.takeException(), isNull, reason: 'Marathi at $width wide, text x$scale');
        }
      }
    });

    testWidgets('an unknown design draws nothing', (tester) async {
      await _pump(tester, _offer(design: 'onam'));
      expect(find.byType(CustomPaint).evaluate().where((e) => (e.widget as CustomPaint).painter is FestivalMotifPainter), isEmpty);
      expect(find.text('FESTIVE OFFER'), findsNothing);
    });
  });

  group('festive touches', () {
    test('every design has a product tag and a Marathi greeting', () {
      for (final t in festivalThemes) {
        expect(festivalTags[t.key], isNotEmpty, reason: t.key);
        expect(RegExp(r'[ऀ-ॿ]').hasMatch(festivalGreetings[t.key] ?? ''), isTrue, reason: t.key);
      }
    });

    test('products of festival offers map to their design; plain offers add none; the first offer wins', () {
      final offers = [
        Offer(id: 'a', title: 'Plain', productIds: const ['p1']),
        Offer(id: 'b', title: 'Diwali', cardDesign: 'diwali', productIds: const ['p2', 'p3']),
        Offer(id: 'c', title: 'Holi', cardDesign: 'holi', productIds: const ['p3', 'p4']),
      ];
      final map = festivalProductThemes(offers);
      expect(map.keys.toSet(), {'p2', 'p3', 'p4'});
      expect(map['p3']?.key, 'diwali');
      expect(map['p4']?.key, 'holi');
      expect(festivalGreetingTheme(offers)?.key, 'diwali');
      expect(festivalGreetingTheme([offers.first]), isNull);
    });

    testWidgets('the tag and the greeting chip show their text without overflow', (tester) async {
      final t = festivalThemeOf('ganesh')!;
      await tester.pumpWidget(MaterialApp(home: Scaffold(body: Column(children: [SizedBox(width: 90, child: FestivalProductTag(t)), FestivalGreetingChip(t)]))));
      expect(find.text('BAPPA SPECIAL'), findsOneWidget);
      expect(find.text('गणपती बाप्पा मोरया'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('the glow over Home shows only while a festival offer runs', (tester) async {
      Future<void> pump(List<Offer> offers) => tester.pumpWidget(ProviderScope(
            overrides: [activeOffersProvider.overrideWith((ref) async => offers)],
            key: UniqueKey(),
            child: const MaterialApp(home: Scaffold(body: SizedBox(height: 300, child: FestivalGlow()))),
          ));

      await pump([Offer(id: 'a', title: 'Plain')]);
      await tester.pumpAndSettle();
      expect(find.byType(DecoratedBox), findsNothing);

      await pump([Offer(id: 'b', title: 'Diwali', cardDesign: 'diwali')]);
      await tester.pumpAndSettle();
      expect(find.descendant(of: find.byType(FestivalGlow), matching: find.byType(DecoratedBox)), findsOneWidget);
    });
  });

  group('reading the design from the API', () {
    test('an offer carries its design, an empty or missing one is the plain banner', () {
      Map<String, dynamic> json(Map<String, dynamic> extra) => {'id': 'o1', 'titleEn': 'Sale', ...extra};

      expect(offerFromJson(json({'cardDesign': 'diwali'})).cardDesign, 'diwali');
      expect(offerFromJson(json({'cardDesign': ''})).cardDesign, isNull);
      expect(offerFromJson(json({'cardDesign': null})).cardDesign, isNull);
      expect(offerFromJson(json({})).cardDesign, isNull);
    });

    test('the card picture link is made whole like any offer picture', () {
      final offer = offerFromJson({'id': 'o1', 'titleEn': 'Sale', 'cardDesign': 'holi', 'imageUrl': '/media/offers/a.jpg'}, imageBase: 'http://host');
      expect((offer.cardDesign, offer.imageUrl), ('holi', 'http://host/media/offers/a.jpg'));
    });
  });
}

