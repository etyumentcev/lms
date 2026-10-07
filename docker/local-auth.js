// Included only in the explicit local-development Docker image target.
window.lmsAuth = {
  getAccessToken: () => sessionStorage.getItem('lms-local-token'),
  onUnauthenticated: () => sessionStorage.removeItem('lms-local-token')
};
