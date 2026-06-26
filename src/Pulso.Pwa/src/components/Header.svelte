<script>
  import { online } from '../lib/stores.js';
</script>

<header>
  <div class="brand">PULSO</div>
  <div class="network-badge" class:online={$online}>
    <span>{$online ? 'PORTAL CONECTADO' : 'SITUACIÓN SINCRONIZADA LOCAL / OFFLINE'}</span>
  </div>
</header>
