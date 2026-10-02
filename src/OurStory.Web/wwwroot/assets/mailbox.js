/* 删除前确认；内容读取与权限校验均由服务端完成。 */
document.querySelectorAll('.mailbox-letter form[data-confirm]').forEach(form => {
  form.addEventListener('submit', event => {
    if (!window.confirm(form.dataset.confirm)) event.preventDefault();
  });
});
