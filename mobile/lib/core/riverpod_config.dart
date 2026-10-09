/// Riverpod 3 retries failed providers automatically. The app shows its own Retry buttons and error
/// messages instead, so retries are turned off to avoid delayed error screens and repeated API calls.
Duration? noAutomaticRetry(int retryCount, Object error) => null;
