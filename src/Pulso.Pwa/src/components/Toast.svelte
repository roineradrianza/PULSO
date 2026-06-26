<script>
  import { toast } from '../lib/stores.js';
</script>

<div class="toast" class:show={$toast.show} class:error={$toast.isError}>
  {$toast.message}
</div>
