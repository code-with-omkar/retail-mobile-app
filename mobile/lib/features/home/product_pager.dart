import 'package:flutter/foundation.dart';

import '../../data/api/api_exception.dart';
import '../../data/models.dart';
import '../../data/repositories/catalog_repository.dart';

/// Loads a product list page by page for one query (search text and/or category).
/// A screen owns one pager per query and disposes it; in-flight results of a disposed pager are ignored.
class ProductPager extends ChangeNotifier {
  ProductPager(this._repo, {this.search, this.categoryId, this.pageSize = 20, this.storeId});

  final CatalogRepository _repo;
  final String? search, categoryId;
  final int pageSize;

  /// Resolves the store whose stock to show. Called once; a failure or null means stock is unknown.
  final Future<String?> Function()? storeId;
  String? _storeId;
  bool _storeResolved = false;

  final items = <Product>[];
  int _page = 0;
  int totalCount = 0;
  bool hasMore = true;
  bool loading = false;
  ApiException? error;
  bool _disposed = false;

  bool get isInitialLoading => loading && items.isEmpty && error == null;
  bool get isEmpty => !loading && error == null && items.isEmpty;

  /// Loads the first page (or starts over).
  Future<void> loadFirst() {
    items.clear();
    _page = 0;
    hasMore = true;
    return _load();
  }

  Future<void> loadMore() => hasMore && !loading ? _load() : Future.value();

  /// Retries the page that failed.
  Future<void> retry() => _page == 0 ? loadFirst() : _load();

  Future<void> _load() async {
    loading = true;
    error = null;
    _notify();
    try {
      if (!_storeResolved) {
        try {
          _storeId = await storeId?.call();
        } catch (_) {
          _storeId = null; // stock stays unknown; the list still loads
        }
        _storeResolved = true;
      }
      // With a store, the list is that store's catalogue; without one (lookup failed, nobody delivers) it is the whole catalogue.
      final result = await _repo.products(search: search, categoryId: categoryId, storeId: _storeId, carriedOnly: _storeId != null, page: _page + 1, pageSize: pageSize);
      if (_disposed) return;
      items.addAll(result.items);
      _page = result.page;
      totalCount = result.totalCount;
      hasMore = result.hasMore;
    } on ApiException catch (e) {
      if (_disposed) return;
      error = e;
    }
    loading = false;
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
