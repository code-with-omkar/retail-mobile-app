import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:quickcart_customer/core/l10n.dart';
import 'package:quickcart_customer/core/theme.dart';
import 'package:quickcart_customer/data/dto/catalog_dto.dart';
import 'package:quickcart_customer/data/models.dart';
import 'package:quickcart_customer/data/repositories/catalog_repository.dart';
import 'package:quickcart_customer/features/home/categories_screen.dart';

Widget _host(Widget child, {Locale locale = const Locale('en')}) => MaterialApp(
  theme: QC.theme(Brightness.light),
  locale: locale,
  supportedLocales: AppStrings.supported,
  localizationsDelegates: const [AppStrings.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
  home: Scaffold(body: child),
);

void main() {
  test('a category from the API remembers the one it sits under', () {
    final sub = categoryFromCatalogDto(const CatalogCategoryDto(id: 'c2', name: 'Milk', parentCategoryId: 'c1'));
    final top = categoryFromCatalogDto(const CatalogCategoryDto(id: 'c1', name: 'Dairy'));
    expect(sub.parentId, 'c1');
    expect(top.parentId, isNull);
  });

  test('a sub-category with no group of its own takes its main category picture, and one with its own group keeps it', () {
    const all = [
      Category('g', 'Groceries', LucideIcons.shoppingBasket),
      Category('dal', 'Dal', LucideIcons.layoutGrid, parentId: 'g'),
      Category('dairy', 'Dairy', LucideIcons.cupSoda),
      Category('curd', 'Curd', LucideIcons.layoutGrid, parentId: 'dairy'),
      Category('milk', 'Milk', LucideIcons.cupSoda, parentId: 'g'),
      Category('misc', 'Misc', LucideIcons.layoutGrid),
    ];
    final keys = categoryKeysOf(all);
    expect(keys['dal'], 'pantry');
    expect(keys['curd'], 'dairy');
    expect(keys['milk'], 'dairy', reason: 'its own name matches the dairy group');
    expect(keys['misc'], 'other');
  });

  testWidgets('the chips are All plus each sub-category, the chosen one is marked, and a tap picks it', (tester) async {
    const subs = [Category('c2', 'Milk', LucideIcons.cupSoda, parentId: 'c1'), Category('c3', 'Curd', LucideIcons.cupSoda, parentId: 'c1')];
    final picked = <String?>[];
    await tester.pumpWidget(_host(SubCategoryChips(subs, 'c2', picked.add)));
    await tester.pumpAndSettle();

    expect(find.text('All'), findsOneWidget);
    expect(find.text('Milk'), findsOneWidget);
    expect(find.text('Curd'), findsOneWidget);

    await tester.tap(find.text('Curd'));
    await tester.tap(find.text('All'));
    expect(picked, ['c3', null]);
  });

  testWidgets('All reads सर्व in Marathi', (tester) async {
    await tester.pumpWidget(_host(SubCategoryChips(const [Category('c2', 'Milk', LucideIcons.cupSoda, parentId: 'c1')], null, (_) {}), locale: const Locale('mr')));
    await tester.pumpAndSettle();
    expect(find.text('सर्व'), findsOneWidget);
  });
}
