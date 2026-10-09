import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/l10n.dart';
import 'core/prefs.dart';
import 'core/theme.dart';
import 'features/address/address_controller.dart';
import 'features/address/address_editor_screen.dart';
import 'features/address/address_list_screen.dart';
import 'features/address/delivery_actions.dart';
import 'features/auth/auth_controller.dart';
import 'features/auth/auth_screens.dart';
import 'features/auth/auth_widgets.dart';
import 'features/cart/cart_screen.dart';
import 'features/checkout/checkout_screen.dart';
import 'features/dev/api_diagnostics_screen.dart';
import 'features/home/categories_screen.dart';
import 'features/home/home_screen.dart';
import 'features/notifications/notifications_screen.dart';
import 'features/orders/order_detail_screen.dart';
import 'features/orders/orders_screen.dart';
import 'features/payment/pay_screen.dart';
import 'features/product/product_screen.dart';
import 'features/profile/account_screens.dart';
import 'features/profile/profile_screen.dart';
import 'features/shell/app_shell.dart';
import 'features/stores/stores_screen.dart';

/// Screens that need a signed-in customer. The API enforces this too; this only saves a guest from a dead end.
const _signInRequired = ['/checkout', '/account'];

/// [auth] is read at navigation time; [refresh] re-runs the redirect when the sign-in state changes.
GoRouter createRouter({AuthState Function()? auth, Listenable? refresh}) => GoRouter(
      refreshListenable: refresh,
      redirect: (_, state) {
        final current = auth?.call();
        // While the stored session is being checked, stay put; the redirect runs again when the answer arrives.
        if (current == null || current.status != AuthStatus.signedOut) return null;
        final path = state.uri.path;
        if (_signInRequired.any((p) => path == p || path.startsWith('$p/'))) return withNext('/welcome', state.uri.toString());
        return null;
      },
      routes: [
        StatefulShellRoute.indexedStack(
          builder: (_, _, shell) => AppShell(shell),
          branches: [
            StatefulShellBranch(routes: [GoRoute(path: '/', builder: (_, _) => const HomeScreen())]),
            StatefulShellBranch(routes: [GoRoute(path: '/categories', builder: (_, _) => const CategoriesScreen())]),
            StatefulShellBranch(routes: [GoRoute(path: '/orders', builder: (_, _) => const OrdersScreen())]),
            StatefulShellBranch(routes: [GoRoute(path: '/profile', builder: (_, _) => const ProfileScreen())]),
          ],
        ),
        if (kDebugMode) GoRoute(path: '/dev/api', builder: (_, _) => const ApiDiagnosticsScreen()),
        GoRoute(path: '/stores', builder: (_, _) => const StoresScreen()),
        GoRoute(path: '/cart', builder: (_, _) => const CartScreen()),
        GoRoute(path: '/category/:id', builder: (_, s) => CategoryProductsScreen(s.pathParameters['id']!)),
        GoRoute(path: '/product/:id', builder: (_, s) => ProductScreen(s.pathParameters['id']!)),
        GoRoute(path: '/welcome', builder: (_, s) => WelcomeScreen(next: s.uri.queryParameters['next'])),
        GoRoute(path: '/login', builder: (_, s) => LoginScreen(next: s.uri.queryParameters['next'])),
        GoRoute(path: '/register', builder: (_, s) => RegisterScreen(next: s.uri.queryParameters['next'])),
        GoRoute(path: '/forgot', builder: (_, _) => const ForgotPasswordScreen()),
        GoRoute(path: '/reset', builder: (_, s) => ResetPasswordScreen(s.uri.queryParameters['email'] ?? '')),
        GoRoute(path: '/account/profile', builder: (_, _) => const EditProfileScreen()),
        GoRoute(path: '/account/password', builder: (_, _) => const ChangePasswordScreen()),
        GoRoute(path: '/address', redirect: (_, _) => '/addresses'),
        GoRoute(path: '/addresses', builder: (_, _) => const AddressListScreen()),
        GoRoute(path: '/notifications', builder: (_, _) => const NotificationsScreen()),
        GoRoute(path: '/addresses/new', builder: (_, s) => AddressEditorScreen(args: s.extra as AddressEditorArgs?)),
        GoRoute(path: '/addresses/:id/edit', builder: (_, s) => _EditAddressRoute(s.pathParameters['id']!, s.extra as AddressEditorArgs?)),
        GoRoute(path: '/checkout', builder: (_, _) => const CheckoutScreen()),
        GoRoute(path: '/pay/:id', builder: (_, s) => PayScreen(s.pathParameters['id']!, auto: s.uri.queryParameters['auto'] == '1')),
        GoRoute(path: '/order-success/:id', builder: (_, s) => OrderSuccessScreen(s.pathParameters['id']!)),
        GoRoute(path: '/order/:id', builder: (_, s) => OrderDetailScreen(s.pathParameters['id']!)),
      ],
    );

/// The edit screen needs the address. It normally arrives with the navigation; after a restart or a deep link it is looked up here.
class _EditAddressRoute extends ConsumerWidget {
  const _EditAddressRoute(this.id, this.args);
  final String id;
  final AddressEditorArgs? args;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (args?.existing != null) return AddressEditorScreen(args: args);
    final saved = ref.watch(savedAddressesProvider);
    final address = saved.value?.where((a) => a.id == id).firstOrNull;
    if (address != null) return AddressEditorScreen(args: AddressEditorArgs(existing: address));
    return const AddressListScreen(); // not found, or still loading: show the list
  }
}

class QuickCartApp extends ConsumerStatefulWidget {
  const QuickCartApp({super.key});

  @override
  ConsumerState<QuickCartApp> createState() => _QuickCartAppState();
}

class _QuickCartAppState extends ConsumerState<QuickCartApp> {
  final _signInChanged = ValueNotifier<int>(0);
  late final _router = createRouter(auth: () => ref.read(authProvider), refresh: _signInChanged);

  @override
  void initState() {
    super.initState();
    ref.listenManual(authProvider, (prev, next) {
      if (prev?.status != next.status) _signInChanged.value++;
    });
  }

  @override
  void dispose() {
    _signInChanged.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => MaterialApp.router(
        title: 'QuickCart',
        debugShowCheckedModeBanner: false,
        theme: QC.theme(Brightness.light),
        darkTheme: QC.theme(Brightness.dark),
        themeMode: ref.watch(themeModeProvider),
        locale: ref.watch(localeProvider),
        supportedLocales: AppStrings.supported,
        localizationsDelegates: const [AppStrings.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
        routerConfig: _router,
      );
}
