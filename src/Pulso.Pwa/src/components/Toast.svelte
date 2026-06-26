<script>
  import { toast } from '../lib/stores.js';
</script>

<div class="toast" class:show={$toast.show} class:error={$toast.type === 'error'} class:info={$toast.type === 'info'}>
  {$toast.message}
</div>
